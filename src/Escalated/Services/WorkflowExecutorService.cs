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
/// Action catalog (<see cref="WorkflowEngine.ActionTypes"/>): <c>change_priority</c>,
/// <c>change_status</c>, <c>assign_agent</c>, <c>set_department</c>, <c>add_tag</c>,
/// <c>remove_tag</c>, <c>add_note</c>, <c>insert_canned_reply</c>, <c>add_follower</c>,
/// <c>set_type</c>, <c>send_webhook</c> and <c>delay</c>, plus Laravel's legacy
/// names <c>move_department</c> and <c>add_internal_note</c>.
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
    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly TimeProvider _time;

    public WorkflowExecutorService(
        EscalatedDbContext db,
        TicketService tickets,
        AssignmentService assignments,
        ILogger<WorkflowExecutorService> logger,
        IHttpClientFactory? httpClientFactory = null,
        TimeProvider? time = null)
    {
        _db = db;
        _tickets = tickets;
        _assignments = assignments;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Execute every action in <paramref name="actionsJson"/> against
    /// <paramref name="ticket"/>. Returns the parsed action list so the
    /// caller (typically <c>WorkflowRunner</c>) can serialize it into a
    /// <see cref="WorkflowLog"/> audit row.
    /// </summary>
    /// <param name="workflowId">
    /// The Workflow the actions belong to. A <c>delay</c> action needs it to hold
    /// the remaining actions back; without it a delay is skipped and the rest run now.
    /// </param>
    public async Task<IReadOnlyList<Dictionary<string, object?>>> ExecuteAsync(
        Ticket ticket, string? actionsJson, CancellationToken ct = default, int? workflowId = null)
    {
        var actions = ParseActions(actionsJson);
        for (var index = 0; index < actions.Count; index++)
        {
            var action = actions[index];
            if (action.GetValueOrDefault("type")?.ToString() == "delay" && workflowId is not null)
            {
                await HoldRemainingAsync(workflowId.Value, ticket, action, actions.Skip(index + 1).ToList(), ct);
                return actions.Take(index + 1).ToList();
            }

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
            case "move_department":
                await SetDepartmentAsync(ticket, value, ct);
                break;
            case "add_tag":
                await AddTagAsync(ticket, value, ct);
                break;
            case "remove_tag":
                await RemoveTagAsync(ticket, value, ct);
                break;
            case "add_note":
            case "add_internal_note":
                await AddNoteAsync(ticket, value, ct);
                break;
            case "set_type":
                await SetTypeAsync(ticket, value, ct);
                break;
            case "send_webhook":
                await SendWebhookAsync(ticket, value, ct);
                break;
            case "delay":
                _logger.LogWarning("[WorkflowExecutor] delay outside a stored Workflow on ticket #{TicketId}; running the rest now", ticket.Id);
                break;
            case "insert_canned_reply":
                await InsertCannedReplyAsync(ticket, value, ct);
                break;
            case "add_follower":
                await AddFollowerAsync(ticket, value, ct);
                break;
            default:
                _logger.LogWarning("[WorkflowExecutor] unknown action type: {Type}", type);
                break;
        }
    }

    private async Task AddFollowerAsync(Ticket ticket, string value, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(value) || value == "0")
        {
            return;
        }

        var exists = await _db.TicketFollowers
            .AnyAsync(f => f.TicketId == ticket.Id && f.UserId == value, ct);
        if (exists)
        {
            return;
        }

        _db.TicketFollowers.Add(new TicketFollower { TicketId = ticket.Id, UserId = value });
        await _db.SaveChangesAsync(ct);
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
        if (string.IsNullOrWhiteSpace(value)) return;
        await _assignments.AssignAsync(ticket, value, ct: ct);
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

    /// <summary>The builder sends the tag's name; slugs and ids are still accepted.</summary>
    private async Task<int?> ResolveTagIdAsync(string value, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var byName = await _db.Tags.FirstOrDefaultAsync(t => t.Name == value, ct);
        if (byName != null) return byName.Id;
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
        var note = WorkflowEngine.InterpolateVariables(body, TicketToMap(ticket));
        await _tickets.AddReplyAsync(ticket, note, authorType: "system", isNote: true, ct: ct);
    }

    private async Task SetTypeAsync(Ticket ticket, string value, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(value) || ticket.TicketType == value.Trim()) return;
        await _tickets.UpdateAsync(ticket, ticketType: value.Trim(), ct: ct);
    }

    /// <summary>
    /// POSTs <c>{ticket: {id, reference, subject, status, priority}}</c> to the URL,
    /// as Laravel's scalar-URL <c>send_webhook</c> does. It goes through the
    /// SSRF-guarded webhook client: http(s) to public addresses only, no redirects.
    /// </summary>
    private async Task SendWebhookAsync(Ticket ticket, string value, CancellationToken ct)
    {
        var url = value.Trim();
        if (!OutboundUrlGuard.TryValidateUri(url, out var uri))
        {
            _logger.LogWarning("[WorkflowExecutor] send_webhook blocked on ticket #{TicketId}: not a public http(s) URL", ticket.Id);
            return;
        }

        if (_httpClientFactory is null)
        {
            _logger.LogWarning("[WorkflowExecutor] send_webhook skipped: no IHttpClientFactory registered");
            return;
        }

        var payload = new Dictionary<string, object?>
        {
            ["ticket"] = new Dictionary<string, object?>
            {
                ["id"] = ticket.Id,
                ["reference"] = ticket.Reference,
                ["subject"] = ticket.Subject,
                ["status"] = ticket.Status.ToValue(),
                ["priority"] = ticket.Priority.ToValue(),
            },
        };

        var client = _httpClientFactory.CreateClient(WebhookDispatcher.HttpClientName);
        client.Timeout = TimeSpan.FromSeconds(10);
        using var content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(uri, content, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("[WorkflowExecutor] send_webhook to {Host} answered {Status}", uri.Host, (int)response.StatusCode);
        }
    }

    /// <summary>
    /// Stores the actions after a <c>delay</c> for <see cref="WorkflowDelayedActionProcessor"/>.
    /// The value is minutes (the builder sends a number or a numeric string; older
    /// Workflows stored <c>{"minutes": n}</c>).
    /// </summary>
    private async Task HoldRemainingAsync(int workflowId, Ticket ticket, Dictionary<string, object?> delay,
        List<Dictionary<string, object?>> remaining, CancellationToken ct)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        _db.WorkflowDelayedActions.Add(new WorkflowDelayedAction
        {
            WorkflowId = workflowId,
            TicketId = ticket.Id,
            Action = JsonSerializer.Serialize(delay),
            RemainingActions = JsonSerializer.Serialize(remaining),
            ExecuteAt = now.AddMinutes(DelayMinutes(delay.GetValueOrDefault("value"))),
            CreatedAt = now,
            UpdatedAt = now,
        });
        await _db.SaveChangesAsync(ct);
    }

    internal static int DelayMinutes(object? value)
    {
        if (value is JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("minutes", out var minutes))
            {
                element = minutes;
            }

            var text = element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString();
            return double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                ? (int)Math.Max(0, Math.Min(parsed, 525_600))
                : 0;
        }

        return double.TryParse(value?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number)
            ? (int)Math.Max(0, Math.Min(number, 525_600))
            : 0;
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
