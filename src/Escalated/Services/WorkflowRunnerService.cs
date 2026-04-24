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
        var workflows = await _db.Workflows
            .Where(w => w.TriggerEvent == triggerEvent && w.IsActive)
            .OrderBy(w => w.Position)
            .ToListAsync(ct);

        if (workflows.Count == 0) return;

        var conditionMap = TicketToConditionMap(ticket);

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
                var executed = await _executor.ExecuteAsync(ticket, wf.Actions, ct);
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

    /// <summary>
    /// Parse the workflow's <see cref="Workflow.Conditions"/> JSON and
    /// evaluate it against the ticket field-map. Null/blank conditions
    /// match everything (NestJS parity).
    /// </summary>
    private bool Evaluate(Workflow wf, Dictionary<string, string> conditionMap)
    {
        if (string.IsNullOrWhiteSpace(wf.Conditions) || wf.Conditions == "{}")
        {
            return true;
        }
        try
        {
            var conditions = JsonSerializer.Deserialize<Dictionary<string, object>>(wf.Conditions)
                             ?? new Dictionary<string, object>();
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
