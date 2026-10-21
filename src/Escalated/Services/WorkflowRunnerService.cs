using System.Text.Json;
using Escalated.Data;
using Escalated.Enums;
using Escalated.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Escalated.Services;

/// <summary>
/// Orchestrates evaluation + execution of Workflows for a given trigger
/// event.
///
/// For each active Workflow matching the trigger (in <see cref="Workflow.Position"/>
/// order), evaluates conditions via <see cref="WorkflowEngine"/> and, if matched,
/// dispatches to <see cref="WorkflowExecutorService"/>. Writes a
/// <see cref="WorkflowLog"/> row per Workflow considered. Honors
/// <see cref="Workflow.StopOnMatch"/>.
///
/// Executor errors are caught so one misbehaving workflow never blocks the
/// rest — the failure is stamped on the log row via
/// <see cref="WorkflowLog.ErrorMessage"/>.
///
/// Mirrors the NestJS reference <c>workflow-runner.service.ts</c>.
/// </summary>
public class WorkflowRunnerService
{
    private readonly EscalatedDbContext _db;
    private readonly WorkflowEngine _engine;
    private readonly WorkflowExecutorService _executor;
    private readonly ILogger<WorkflowRunnerService> _logger;

    public WorkflowRunnerService(
        EscalatedDbContext db,
        WorkflowEngine engine,
        WorkflowExecutorService executor,
        ILogger<WorkflowRunnerService> logger)
    {
        _db = db;
        _engine = engine;
        _executor = executor;
        _logger = logger;
    }

    public async Task RunForEventAsync(string triggerEvent, Ticket ticket, CancellationToken ct = default)
    {
        // Workflows saved under a legacy name (ADR 2026-10-21, e.g. `ticket.replied`)
        // keep firing on the canonical event.
        var names = Escalated.Events.EventNames.NamesFor(Escalated.Events.EventNames.Canonicalize(triggerEvent)).ToArray();
        var workflows = await _db.Workflows
            .Where(w => names.Contains(w.TriggerEvent) && w.IsActive)
            .OrderBy(w => w.Position)
            .ToListAsync(ct);

        if (workflows.Count == 0) return;

        var conditionMap = await ConditionMapAsync(ticket, ct);

        foreach (var wf in workflows)
        {
            var startedAt = DateTime.UtcNow;
            var matched = Evaluate(wf, conditionMap);

            var log = new WorkflowLog
            {
                WorkflowId = wf.Id,
                TicketId = ticket.Id,
                TriggerEvent = triggerEvent,
                ConditionsMatched = matched,
                StartedAt = startedAt,
                CreatedAt = startedAt,
            };
            _db.WorkflowLogs.Add(log);
            await _db.SaveChangesAsync(ct);

            if (!matched) continue;

            try
            {
                var executed = await _executor.ExecuteAsync(ticket, wf.Actions, ct, wf.Id);
                log.ActionsExecutedJson = SafeSerialize(executed);
                log.CompletedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Workflow #{WorkflowId} ({Name}) failed on ticket #{TicketId}",
                    wf.Id, wf.Name, ticket.Id);
                log.ErrorMessage = ex.Message;
                log.CompletedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
            }

            if (wf.StopOnMatch) break;
        }
    }

    /// <summary>Whether <paramref name="wf"/>'s conditions match <paramref name="ticket"/> as stored now.</summary>
    internal async Task<bool> MatchesAsync(Workflow wf, Ticket ticket, CancellationToken ct = default) =>
        Evaluate(wf, await ConditionMapAsync(ticket, ct));

    /// <summary>
    /// Parse the workflow's <see cref="Workflow.Conditions"/> JSON and
    /// evaluate it against the ticket field-map. Null/blank conditions
    /// match everything (NestJS parity). Besides the contract's
    /// <c>{all}</c>/<c>{any}</c>, it reads the shapes older Workflows were stored
    /// in: a flat list (treated as <c>all</c>) and Laravel's
    /// <c>{match, rules}</c> (or the builder's old <c>{match, conditions}</c>).
    /// </summary>
    private bool Evaluate(Workflow wf, Dictionary<string, string> conditionMap)
    {
        if (string.IsNullOrWhiteSpace(wf.Conditions) || wf.Conditions == "{}")
        {
            return true;
        }
        try
        {
            using var doc = JsonDocument.Parse(wf.Conditions);
            var root = doc.RootElement;
            var conditions = new Dictionary<string, object>();
            if (root.ValueKind == JsonValueKind.Array)
            {
                conditions["all"] = root.Clone();
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("all", out var all)) conditions["all"] = all.Clone();
                else if (root.TryGetProperty("any", out var any)) conditions["any"] = any.Clone();
                else if (root.TryGetProperty("rules", out var rules) || root.TryGetProperty("conditions", out rules))
                {
                    var match = root.TryGetProperty("match", out var m) && m.ValueKind == JsonValueKind.String && m.GetString() == "any" ? "any" : "all";
                    conditions[match] = rules.Clone();
                }
            }
            return _engine.EvaluateConditions(conditions, conditionMap);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex,
                "[WorkflowRunner] bad conditions JSON on workflow #{WorkflowId}", wf.Id);
            return false;
        }
    }

    private static string SafeSerialize(object value)
    {
        try { return JsonSerializer.Serialize(value); }
        catch { return "[]"; }
    }

    /// <summary>
    /// The ticket as the condition fields see it, with its tag names
    /// (newline-separated, read as a list by <see cref="WorkflowEngine"/>) and the
    /// <c>hours_since_*</c> ages.
    /// </summary>
    private async Task<Dictionary<string, string>> ConditionMapAsync(Ticket ticket, CancellationToken ct)
    {
        var map = TicketToConditionMap(ticket);
        var tags = await _db.TicketTags.AsNoTracking()
            .Where(tt => tt.TicketId == ticket.Id)
            .Join(_db.Tags, tt => tt.TagId, t => t.Id, (tt, t) => t.Name)
            .ToListAsync(ct);
        map["tags"] = string.Join(WorkflowEngine.ListSeparator, tags);
        var now = DateTime.UtcNow;
        map["hours_since_created"] = Math.Floor((now - ticket.CreatedAt).TotalHours).ToString(System.Globalization.CultureInfo.InvariantCulture);
        map["hours_since_updated"] = Math.Floor((now - ticket.UpdatedAt).TotalHours).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return map;
    }

    private static Dictionary<string, string> TicketToConditionMap(Ticket ticket)
    {
        var map = new Dictionary<string, string>();
        Put(map, "id", ticket.Id);
        Put(map, "subject", ticket.Subject);
        Put(map, "description", ticket.Description);
        Put(map, "reference", ticket.Reference);
        Put(map, "requester_id", ticket.RequesterId);
        Put(map, "requester_type", ticket.RequesterType);
        Put(map, "department_id", ticket.DepartmentId);
        Put(map, "assigned_to", ticket.AssignedTo);
        Put(map, "ticket_type", ticket.TicketType);
        Put(map, "account_type", ticket.AccountType);
        Put(map, "account_id", ticket.AccountId);
        Put(map, "guest_name", ticket.GuestName);
        Put(map, "guest_email", ticket.GuestEmail);
        map["priority"] = ticket.Priority.ToValue();
        map["status"] = ticket.Status.ToValue();
        return map;
    }

    private static void Put(Dictionary<string, string> map, string key, object? value)
    {
        map[key] = value?.ToString() ?? string.Empty;
    }
}
