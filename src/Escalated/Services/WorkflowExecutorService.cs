using System.Text.Json;
using Escalated.Data;
using Escalated.Enums;
using Escalated.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Escalated.Services;

/// <summary>
/// Performs the side-effects dictated by a matched <see cref="Workflow"/>.
///
/// Distinct from <see cref="WorkflowEngine"/>, which only evaluates
/// conditions. This service parses the JSON action array stored on
/// <see cref="Workflow.Actions"/> and dispatches each entry against
/// <see cref="TicketService"/> / <see cref="AssignmentService"/> /
/// the EF Core context.
///
/// Action catalog: <c>change_priority</c>, <c>change_status</c>,
/// <c>assign_agent</c>, <c>set_department</c>, <c>add_tag</c>,
/// <c>remove_tag</c>, <c>add_note</c>, <c>insert_canned_reply</c>.
/// Mirrors the NestJS reference impl in
/// <c>escalated-nestjs/src/services/workflow-executor.service.ts</c>.
///
/// One failing action does not halt the others (warn-logged). Unknown
/// action types warn-log and skip. Malformed JSON returns an empty
/// action list (no NRE).
/// </summary>
public class WorkflowExecutorService
{
    private readonly EscalatedDbContext _db;
    private readonly TicketService _tickets;
    private readonly AssignmentService _assignments;
    private readonly ILogger<WorkflowExecutorService> _logger;

    public WorkflowExecutorService(
        EscalatedDbContext db,
        TicketService tickets,
        AssignmentService assignments,
        ILogger<WorkflowExecutorService> logger)
    {
        _db = db;
        _tickets = tickets;
        _assignments = assignments;
        _logger = logger;
    }

    /// <summary>
    /// Execute every action in <paramref name="actionsJson"/> against
    /// <paramref name="ticket"/>. Returns the parsed action list so the
    /// caller (typically <c>WorkflowRunner</c>) can serialize it into a
    /// <see cref="WorkflowLog"/> audit row.
    /// </summary>
    public async Task<IReadOnlyList<Dictionary<string, object?>>> ExecuteAsync(
        Ticket ticket, string? actionsJson, CancellationToken ct = default)
    {
        var actions = ParseActions(actionsJson);
        foreach (var action in actions)
        {
            try
            {
                await DispatchAsync(ticket, action, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[WorkflowExecutor] action {Type} failed on ticket #{TicketId}",
                    action.GetValueOrDefault("type"), ticket.Id);
            }
        }

        return actions;
    }

    private IReadOnlyList<Dictionary<string, object?>> ParseActions(string? actionsJson)
    {
        if (string.IsNullOrWhiteSpace(actionsJson))
        {
            return Array.Empty<Dictionary<string, object?>>();
        }
        try
        {
            return JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(actionsJson)
                   ?? new List<Dictionary<string, object?>>();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "[WorkflowExecutor] failed to parse actions JSON");
            return Array.Empty<Dictionary<string, object?>>();
        }
    }

    private async Task DispatchAsync(Ticket ticket, Dictionary<string, object?> action, CancellationToken ct)
    {
        var type = action.GetValueOrDefault("type")?.ToString() ?? string.Empty;
        var value = action.GetValueOrDefault("value")?.ToString() ?? string.Empty;
        switch (type)
        {
            case "change_priority":
                await ChangePriorityAsync(ticket, value, ct);
                break;
            case "change_status":
                await ChangeStatusAsync(ticket, value, ct);
                break;
            case "assign_agent":
                await AssignAgentAsync(ticket, value, ct);
                break;
            case "set_department":
                await SetDepartmentAsync(ticket, value, ct);
                break;
            case "add_tag":
                await AddTagAsync(ticket, value, ct);
                break;
            case "remove_tag":
                await RemoveTagAsync(ticket, value, ct);
                break;
            case "add_note":
                await AddNoteAsync(ticket, value, ct);
                break;
            case "insert_canned_reply":
                await InsertCannedReplyAsync(ticket, value, ct);
                break;
            default:
                _logger.LogWarning("[WorkflowExecutor] unknown action type: {Type}", type);
                break;
        }
    }

    private async Task ChangePriorityAsync(Ticket ticket, string value, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var priority = TicketPriorityExtensions.Parse(value.ToLowerInvariant());
        await _tickets.ChangePriorityAsync(ticket, priority, ct: ct);
    }

    private async Task ChangeStatusAsync(Ticket ticket, string value, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var status = TicketStatusExtensions.Parse(value.ToLowerInvariant());
        await _tickets.ChangeStatusAsync(ticket, status, ct: ct);
    }

    private async Task AssignAgentAsync(Ticket ticket, string value, CancellationToken ct)
    {
        if (!int.TryParse(value, out var agentId) || agentId <= 0) return;
        await _assignments.AssignAsync(ticket, agentId, ct: ct);
    }

    private async Task SetDepartmentAsync(Ticket ticket, string value, CancellationToken ct)
    {
        if (!int.TryParse(value, out var deptId) || deptId <= 0) return;
        await _tickets.ChangeDepartmentAsync(ticket, deptId, ct: ct);
    }

    private async Task AddTagAsync(Ticket ticket, string value, CancellationToken ct)
    {
        var tagId = await ResolveTagIdAsync(value, ct);
        if (tagId is null)
        {
            _logger.LogDebug("[WorkflowExecutor] add_tag: tag '{Value}' not found", value);
            return;
        }
        await _tickets.AddTagsAsync(ticket, new[] { tagId.Value }, ct: ct);
    }

    private async Task RemoveTagAsync(Ticket ticket, string value, CancellationToken ct)
    {
        var tagId = await ResolveTagIdAsync(value, ct);
        if (tagId is null) return;
        await _tickets.RemoveTagsAsync(ticket, new[] { tagId.Value }, ct: ct);
    }

    private async Task<int?> ResolveTagIdAsync(string value, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var bySlug = await _db.Tags.FirstOrDefaultAsync(t => t.Slug == value, ct);
        if (bySlug != null) return bySlug.Id;
        if (int.TryParse(value, out var asId) && asId > 0)
        {
            var byId = await _db.Tags.FirstOrDefaultAsync(t => t.Id == asId, ct);
            if (byId != null) return byId.Id;
        }
        return null;
    }

    private async Task AddNoteAsync(Ticket ticket, string body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body)) return;
        await _tickets.AddReplyAsync(ticket, body, authorType: "system", isNote: true, ct: ct);
    }

    /// <summary>
    /// Insert an agent-visible reply built from a template. {{field}}
    /// placeholders interpolated against the ticket via
    /// <see cref="WorkflowEngine.InterpolateVariables"/>. Unknown vars
    /// stay as literal {{...}} so the reader can see the gap.
    /// </summary>
    private async Task InsertCannedReplyAsync(Ticket ticket, string template, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(template)) return;
        var body = WorkflowEngine.InterpolateVariables(template, TicketToMap(ticket));
        await _tickets.AddReplyAsync(ticket, body, authorType: "system", isNote: false, ct: ct);
    }

    private static Dictionary<string, string> TicketToMap(Ticket ticket)
    {
        var map = new Dictionary<string, string>();
        if (ticket.Subject is not null) map["subject"] = ticket.Subject;
        if (ticket.Description is not null) map["description"] = ticket.Description;
        if (ticket.Reference is not null) map["reference"] = ticket.Reference;
        if (ticket.GuestName is not null) map["guest_name"] = ticket.GuestName;
        if (ticket.GuestEmail is not null) map["guest_email"] = ticket.GuestEmail;
        map["priority"] = ticket.Priority.ToValue();
        map["status"] = ticket.Status.ToValue();
        return map;
    }
}
