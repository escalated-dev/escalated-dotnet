using System.Text.Json;
using Escalated.Authorization;
using Escalated.Data;
using Escalated.Events;
using Escalated.Models;
using Escalated.Services;
using Escalated.Ui;
using Escalated.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Controllers.Admin;

/// <summary>
/// The Workflow admin, as JSON endpoints, per escalated-developer-context
/// <c>domain-model/workflow-admin-contract.md</c>. Create and update take the
/// shared builder's body (top-level <c>name</c>, <c>description</c>,
/// <c>trigger_event</c>, <c>conditions</c> as <c>{all}</c> or <c>{any}</c>,
/// <c>actions</c> as <c>{type, value}</c>, <c>is_active</c>). Responses use the
/// contract's snake_case workflow object. Validation failures answer 422 with
/// <c>{message, errors: {field: [..]}}</c>.
///
/// <para>With <c>Escalated:Ui:Enabled</c> on, the same routes also serve the
/// <c>Escalated/Admin/Workflows/Index</c> and <c>Form</c> pages: a page request
/// (an Inertia visit, or a browser GET) gets the page, and a page form post gets a
/// 303 back to the list with a flash, or back to the form with its errors, as
/// escalated-laravel's <c>WorkflowController</c> answers. JSON callers are unchanged.</para>
/// </summary>
[ApiController]
[Route("support/admin/workflows")]
[Authorize(Policy = EscalatedPolicies.Admin)]
public class AdminWorkflowController : ControllerBase
{
    /// <summary>How many executions the logs endpoint returns, newest first (as Laravel).</summary>
    private const int LogsLimit = 100;

    private static readonly Dictionary<string, string> TriggerLabels = new()
    {
        [EventNames.TicketCreated] = "Ticket Created",
        [EventNames.TicketUpdated] = "Ticket Updated",
        [EventNames.TicketStatusChanged] = "Ticket Status Changed",
        [EventNames.TicketResolved] = "Ticket Resolved",
        [EventNames.TicketClosed] = "Ticket Closed",
        [EventNames.TicketReopened] = "Ticket Reopened",
        [EventNames.TicketAssigned] = "Ticket Assigned",
        [EventNames.TicketUnassigned] = "Ticket Unassigned",
        [EventNames.TicketEscalated] = "Ticket Escalated",
        [EventNames.TicketPriorityChanged] = "Ticket Priority Changed",
        [EventNames.TicketDepartmentChanged] = "Ticket Department Changed",
        [EventNames.TicketTagAdded] = "Tag Added",
        [EventNames.TicketTagRemoved] = "Tag Removed",
        [EventNames.ReplyCreated] = "Reply Created",
        [EventNames.NoteCreated] = "Internal Note Added",
        [EventNames.SlaWarning] = "SLA Warning",
        [EventNames.SlaBreached] = "SLA Breached",
        [EventNames.ChatStarted] = "Chat Started",
        [EventNames.ChatAssigned] = "Chat Assigned",
        [EventNames.ChatMessage] = "Chat Message",
        [EventNames.ChatEnded] = "Chat Ended",
    };

    private static readonly Dictionary<string, string> ActionLabels = new()
    {
        ["change_status"] = "Change Status",
        ["change_priority"] = "Change Priority",
        ["add_tag"] = "Add Tag",
        ["remove_tag"] = "Remove Tag",
        ["set_department"] = "Set Department",
        ["assign_agent"] = "Assign Agent",
        ["add_note"] = "Add Internal Note",
        ["insert_canned_reply"] = "Send Canned Reply",
        ["add_follower"] = "Add Follower",
        ["delay"] = "Delay (minutes)",
        ["send_webhook"] = "Send Webhook",
        ["set_type"] = "Set Ticket Type",
    };

    private readonly EscalatedDbContext _db;
    private readonly AuditLogService _audit;
    private readonly EscalatedOptions _options;

    public AdminWorkflowController(EscalatedDbContext db, AuditLogService audit, IOptions<EscalatedOptions> options)
    {
        _db = db;
        _audit = audit;
        _options = options.Value;
    }

    /// <summary>A request for the page rather than the JSON, with the UI on.</summary>
    private bool IsPage => _options.Ui.Enabled && EscalatedPageAttribute.IsPageRequest(Request);

    private string IndexUrl => $"/{_options.RoutePrefix.Trim('/')}/admin/workflows";

    private IActionResult ToIndex(string success) => new UiRedirectResult(IndexUrl) { Success = success };

    /// <summary>Back to the form with the first message of each field, as the page shows them.</summary>
    private IActionResult BackWithErrors(Dictionary<string, List<string>> errors, int? id) =>
        UiResults.Back(HttpContext, id is null ? $"{IndexUrl}/create" : $"{IndexUrl}/{id}",
            errors: errors.ToDictionary(e => e.Key, e => e.Value.First()));

    private async Task<IActionResult> IndexPageAsync(List<Workflow> workflows, CancellationToken ct)
    {
        var ids = workflows.Select(w => w.Id).ToList();
        var stats = await _db.WorkflowLogs.AsNoTracking().IgnoreQueryFilters().Where(l => ids.Contains(l.WorkflowId))
            .GroupBy(l => l.WorkflowId)
            .Select(g => new { Id = g.Key, Count = g.Count(), Last = g.Max(l => l.CreatedAt) })
            .ToDictionaryAsync(x => x.Id, ct);

        return UiResults.Render("Escalated/Admin/Workflows/Index", new
        {
            workflows = workflows.Select(w =>
            {
                var wire = ToWire(w);
                wire["trigger_count"] = stats.TryGetValue(w.Id, out var s) ? s.Count : 0;
                wire["last_triggered_at"] = stats.TryGetValue(w.Id, out var last) ? last.Last : null;
                return wire;
            }).ToList(),
        });
    }

    private static IActionResult FormPage(Workflow? workflow)
    {
        var props = FormOptions();
        props["workflow"] = workflow is null ? null : ToWire(workflow);
        return UiResults.Render("Escalated/Admin/Workflows/Form", props);
    }

    /// <summary>The Form page for a new workflow.</summary>
    [HttpGet("create")]
    [EscalatedPage]
    public IActionResult Create() => FormPage(null);

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var workflows = await _db.Workflows.AsNoTracking().OrderBy(w => w.Position).ThenBy(w => w.Id).ToListAsync(ct);
        if (IsPage) return await IndexPageAsync(workflows, ct);
        return Ok(new { workflows = workflows.Select(ToWire).ToList() });
    }

    /// <summary>The Form page's option lists: what fires, what the executor runs, and the operators.</summary>
    [HttpGet("options")]
    public IActionResult Options() => Ok(FormOptions());

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Show(int id, CancellationToken ct)
    {
        var workflow = await _db.Workflows.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id, ct);
        if (workflow is null) return NotFound();
        if (IsPage) return FormPage(workflow);

        var body = FormOptions();
        body["workflow"] = ToWire(workflow);
        return Ok(body);
    }

    [HttpPost("")]
    public async Task<IActionResult> Store([FromBody] JsonElement body, CancellationToken ct)
    {
        var (input, errors) = Validate(body);
        if (input is null) return IsPage ? BackWithErrors(errors, null) : Invalid(errors);

        var position = await _db.Workflows.AnyAsync(ct) ? await _db.Workflows.MaxAsync(w => w.Position, ct) + 1 : 0;
        var workflow = new Workflow { Position = position, CreatedAt = DateTime.UtcNow };
        input.ApplyTo(workflow);
        _db.Workflows.Add(workflow);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(nameof(Workflow), workflow.Id, "workflow.created", this.CurrentUserId(),
            newValues: new { name = workflow.Name, trigger_event = workflow.TriggerEvent }, ct: ct);

        if (IsPage) return ToIndex("Workflow created.");
        return StatusCode(StatusCodes.Status201Created, new { workflow = ToWire(workflow) });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] JsonElement body, CancellationToken ct)
    {
        var workflow = await _db.Workflows.FirstOrDefaultAsync(w => w.Id == id, ct);
        if (workflow is null) return NotFound();

        var (input, errors) = Validate(body);
        if (input is null) return IsPage ? BackWithErrors(errors, id) : Invalid(errors);

        var old = new { name = workflow.Name, trigger_event = workflow.TriggerEvent };
        input.ApplyTo(workflow);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(nameof(Workflow), workflow.Id, "workflow.updated", this.CurrentUserId(),
            oldValues: old, newValues: new { name = workflow.Name, trigger_event = workflow.TriggerEvent }, ct: ct);

        if (IsPage) return ToIndex("Workflow updated.");
        return Ok(new { workflow = ToWire(workflow) });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Destroy(int id, CancellationToken ct)
    {
        var workflow = await _db.Workflows.FirstOrDefaultAsync(w => w.Id == id, ct);
        if (workflow is null) return NotFound();

        _db.WorkflowDelayedActions.RemoveRange(await _db.WorkflowDelayedActions.Where(d => d.WorkflowId == id).ToListAsync(ct));
        _db.WorkflowLogs.RemoveRange(await _db.WorkflowLogs.IgnoreQueryFilters().Where(l => l.WorkflowId == id).ToListAsync(ct));
        _db.Workflows.Remove(workflow);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(nameof(Workflow), id, "workflow.deleted", this.CurrentUserId(),
            oldValues: new { name = workflow.Name, trigger_event = workflow.TriggerEvent }, ct: ct);

        if (IsPage) return ToIndex("Workflow deleted.");
        return NoContent();
    }

    [HttpPost("{id:int}/toggle")]
    public async Task<IActionResult> Toggle(int id, CancellationToken ct)
    {
        var workflow = await _db.Workflows.FirstOrDefaultAsync(w => w.Id == id, ct);
        if (workflow is null) return NotFound();

        workflow.IsActive = !workflow.IsActive;
        workflow.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        if (IsPage) return ToIndex(workflow.IsActive ? "Workflow enabled." : "Workflow disabled.");
        return Ok(new { workflow = ToWire(workflow) });
    }

    /// <summary>Body: <c>{ "workflow_ids": [3, 1, 2] }</c> in the new order.</summary>
    [HttpPost("reorder")]
    public async Task<IActionResult> Reorder([FromBody] JsonElement body, CancellationToken ct)
    {
        if (body.ValueKind != JsonValueKind.Object
            || !body.TryGetProperty("workflow_ids", out var ids)
            || ids.ValueKind != JsonValueKind.Array
            || ids.EnumerateArray().Any(i => i.ValueKind != JsonValueKind.Number || !i.TryGetInt32(out _)))
        {
            var invalid = new Dictionary<string, List<string>> { ["workflow_ids"] = new() { "The workflow ids must be a list of integers." } };
            return IsPage ? UiResults.Back(HttpContext, IndexUrl, errors: invalid.ToDictionary(e => e.Key, e => e.Value.First())) : Invalid(invalid);
        }

        var order = ids.EnumerateArray().Select(i => i.GetInt32()).ToList();
        var workflows = await _db.Workflows.Where(w => order.Contains(w.Id)).ToListAsync(ct);
        foreach (var workflow in workflows)
        {
            workflow.Position = order.IndexOf(workflow.Id);
            workflow.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        if (IsPage) return ToIndex("Workflows reordered.");
        return Ok(new { message = "Workflows reordered." });
    }

    /// <summary>
    /// The Logs page: the newest executions (filter with <c>?workflow=id</c>), and
    /// the workflows to filter by.
    /// </summary>
    [HttpGet("logs")]
    public async Task<IActionResult> Logs([FromQuery] int? workflow, CancellationToken ct)
    {
        var query = _db.WorkflowLogs.AsNoTracking().Include(l => l.Workflow).Include(l => l.Ticket).AsQueryable();
        if (workflow is > 0)
        {
            query = query.Where(l => l.WorkflowId == workflow);
        }

        var logs = await query.OrderByDescending(l => l.CreatedAt).ThenByDescending(l => l.Id).Take(LogsLimit).ToListAsync(ct);
        var workflows = await _db.Workflows.AsNoTracking().OrderBy(w => w.Position).ThenBy(w => w.Id)
            .Select(w => new { id = w.Id, name = w.Name }).ToListAsync(ct);

        return Ok(new { logs = logs.Select(LogToWire).ToList(), workflows });
    }

    [HttpGet("{id:int}/logs")]
    public Task<IActionResult> WorkflowLogs(int id, CancellationToken ct) => Logs(id, ct);

    private static Dictionary<string, object?> FormOptions() => new()
    {
        ["trigger_events"] = WorkflowEngine.TriggerEvents
            .Select(v => new { value = v, label = TriggerLabels.GetValueOrDefault(v, v) }).ToList(),
        ["action_types"] = WorkflowEngine.ActionTypes
            .Select(v => new { value = v, label = ActionLabels.GetValueOrDefault(v, v) }).ToList(),
        ["operators"] = WorkflowEngine.Operators
            .Select(v => new { value = v, label = v.Replace('_', ' ') }).ToList(),
    };

    /// <summary>The contract's workflow object.</summary>
    internal static Dictionary<string, object?> ToWire(Workflow workflow) => new()
    {
        ["id"] = workflow.Id,
        ["name"] = workflow.Name,
        ["description"] = workflow.Description,
        ["trigger_event"] = EventNames.Canonicalize(workflow.TriggerEvent),
        ["trigger"] = EventNames.Canonicalize(workflow.TriggerEvent),
        ["conditions"] = ParseOr(workflow.Conditions, """{"all":[]}"""),
        ["actions"] = ParseOr(workflow.Actions, "[]"),
        ["is_active"] = workflow.IsActive,
        ["position"] = workflow.Position,
        ["stop_on_match"] = workflow.StopOnMatch,
        ["created_at"] = workflow.CreatedAt,
        ["updated_at"] = workflow.UpdatedAt,
    };

    private static Dictionary<string, object?> LogToWire(WorkflowLog log) => new()
    {
        ["id"] = log.Id,
        ["workflow_id"] = log.WorkflowId,
        ["ticket_id"] = log.TicketId,
        ["event"] = log.TriggerEvent,
        ["trigger_event"] = log.TriggerEvent,
        ["workflow_name"] = log.Workflow?.Name,
        ["ticket_reference"] = log.Ticket?.Reference,
        ["matched"] = log.ConditionsMatched,
        ["conditions_matched"] = log.ConditionsMatched,
        ["actions_executed"] = log.ActionsExecutedCount,
        ["action_details"] = log.ActionDetails,
        ["duration_ms"] = log.DurationMs,
        ["status"] = log.Status,
        ["error_message"] = log.ErrorMessage,
        ["started_at"] = log.StartedAt,
        ["completed_at"] = log.CompletedAt,
        ["created_at"] = log.CreatedAt,
    };

    private static JsonElement ParseOr(string? json, string fallback)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? fallback : json);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            using var doc = JsonDocument.Parse(fallback);
            return doc.RootElement.Clone();
        }
    }

    private UnprocessableEntityObjectResult Invalid(Dictionary<string, List<string>> errors) =>
        UnprocessableEntity(new { message = errors.Values.First().First(), errors });

    private sealed record WorkflowInput(string Name, string? Description, string TriggerEvent, string Conditions, string Actions, bool IsActive)
    {
        public void ApplyTo(Workflow workflow)
        {
            workflow.Name = Name;
            workflow.Description = Description;
            workflow.TriggerEvent = TriggerEvent;
            workflow.Conditions = Conditions;
            workflow.Actions = Actions;
            workflow.IsActive = IsActive;
            workflow.UpdatedAt = DateTime.UtcNow;
        }
    }

    private static bool IsScalar(JsonElement value) =>
        value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null;

    /// <summary>The contract's validation; a legacy trigger name is stored as its canonical name.</summary>
    private static (WorkflowInput? Input, Dictionary<string, List<string>> Errors) Validate(JsonElement body)
    {
        var errors = new Dictionary<string, List<string>>();
        void Error(string field, string message)
        {
            if (!errors.TryGetValue(field, out var list)) errors[field] = list = new List<string>();
            list.Add(message);
        }

        if (body.ValueKind != JsonValueKind.Object)
        {
            Error("name", "The request body must be a JSON object.");
            return (null, errors);
        }

        string? name = null;
        if (!body.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(nameEl.GetString()))
            Error("name", "The name field is required.");
        else if (nameEl.GetString()!.Trim().Length > 255)
            Error("name", "The name may not be greater than 255 characters.");
        else
            name = nameEl.GetString()!.Trim();

        string? description = null;
        if (body.TryGetProperty("description", out var descEl) && descEl.ValueKind != JsonValueKind.Null)
        {
            if (descEl.ValueKind != JsonValueKind.String) Error("description", "The description must be a string.");
            else description = string.IsNullOrWhiteSpace(descEl.GetString()) ? null : descEl.GetString();
        }

        string? trigger = null;
        if (!body.TryGetProperty("trigger_event", out var triggerEl) || triggerEl.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(triggerEl.GetString()))
        {
            Error("trigger_event", "The trigger event field is required.");
        }
        else
        {
            trigger = EventNames.Canonicalize(triggerEl.GetString()!);
            if (!WorkflowEngine.TriggerEvents.Contains(trigger))
                Error("trigger_event", $"The trigger event \"{triggerEl.GetString()}\" is not one this backend fires.");
        }

        var conditions = """{"all":[]}""";
        if (body.TryGetProperty("conditions", out var condEl) && condEl.ValueKind != JsonValueKind.Null)
        {
            JsonElement list = default;
            var match = "all";
            if (condEl.ValueKind == JsonValueKind.Array)
            {
                list = condEl;
            }
            else if (condEl.ValueKind == JsonValueKind.Object)
            {
                var keys = condEl.EnumerateObject().Select(p => p.Name).ToList();
                var hasAll = keys.Contains("all");
                var hasAny = keys.Contains("any");
                if (hasAll == hasAny)
                {
                    if (keys.Count > 0) Error("conditions", "The conditions must have exactly one of \"all\" or \"any\".");
                }
                else
                {
                    match = hasAll ? "all" : "any";
                    list = condEl.GetProperty(match);
                }
            }
            else
            {
                Error("conditions", "The conditions must be an object.");
            }

            if (list.ValueKind != JsonValueKind.Undefined)
            {
                if (list.ValueKind != JsonValueKind.Array)
                {
                    Error($"conditions.{match}", "The conditions must be a list.");
                }
                else
                {
                    var i = 0;
                    foreach (var condition in list.EnumerateArray())
                    {
                        if (condition.ValueKind != JsonValueKind.Object
                            || !condition.TryGetProperty("field", out var f) || f.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(f.GetString()))
                            Error($"conditions.{match}.{i}.field", "Each condition needs a field.");
                        else if (!condition.TryGetProperty("operator", out var op) || op.ValueKind != JsonValueKind.String || !WorkflowEngine.Operators.Contains(op.GetString()))
                            Error($"conditions.{match}.{i}.operator", "Each condition needs a known operator.");
                        else if (condition.TryGetProperty("value", out var v) && !IsScalar(v))
                            Error($"conditions.{match}.{i}.value", "A condition value must be a scalar.");
                        i++;
                    }

                    conditions = JsonSerializer.Serialize(new Dictionary<string, JsonElement> { [match] = list });
                }
            }
        }

        var actions = "[]";
        if (!body.TryGetProperty("actions", out var actionsEl) || actionsEl.ValueKind != JsonValueKind.Array || actionsEl.GetArrayLength() == 0)
        {
            Error("actions", "At least one action is required.");
        }
        else
        {
            var normalized = new List<Dictionary<string, object?>>();
            var i = 0;
            foreach (var action in actionsEl.EnumerateArray())
            {
                if (action.ValueKind != JsonValueKind.Object
                    || !action.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String
                    || !(WorkflowEngine.ActionTypes.Contains(type.GetString()) || WorkflowEngine.LegacyActionTypes.Contains(type.GetString())))
                {
                    Error($"actions.{i}.type", "Each action needs a type this backend runs.");
                }
                else if (action.TryGetProperty("value", out var value) && !IsScalar(value))
                {
                    Error($"actions.{i}.value", "An action value must be a scalar.");
                }
                else
                {
                    normalized.Add(new Dictionary<string, object?>
                    {
                        ["type"] = type.GetString(),
                        ["value"] = action.TryGetProperty("value", out var v) ? v.Clone() : string.Empty,
                    });
                }
                i++;
            }

            actions = JsonSerializer.Serialize(normalized);
        }

        var isActive = true;
        if (body.TryGetProperty("is_active", out var activeEl))
        {
            if (activeEl.ValueKind is JsonValueKind.True or JsonValueKind.False) isActive = activeEl.GetBoolean();
            else if (activeEl.ValueKind != JsonValueKind.Null) Error("is_active", "The is active field must be true or false.");
        }

        if (errors.Count > 0) return (null, errors);
        return (new WorkflowInput(name!, description, trigger!, conditions, actions, isActive), errors);
    }
}
