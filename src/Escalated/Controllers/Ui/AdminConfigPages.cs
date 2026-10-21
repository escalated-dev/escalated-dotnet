using System.Globalization;
using System.Text.Json;
using Escalated.Authorization;
using Escalated.Enums;
using Escalated.Events;
using Escalated.Models;
using Escalated.Services;
using Escalated.Ui;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Controllers.Ui;

/// <summary>
/// The admin configuration pages, with escalated-laravel's routes and props:
/// <c>SlaPolicies/Index</c> and <c>Form</c> (<c>SlaPolicyController</c>),
/// <c>EscalationRules/Index</c> and <c>Form</c> (<c>EscalationRuleController</c>),
/// <c>CannedResponses/Index</c>, <c>Macros/Index</c>, <c>Statuses/Index</c> and
/// <c>Form</c> (<c>StatusController</c>), and <c>Webhooks/Index</c>, <c>Form</c> and
/// <c>DeliveryLog</c> (<c>WebhookController</c>). The list routes and the writes the
/// JSON admin API also answers are negotiated, so API clients are unchanged.
/// </summary>
public partial class AdminPageController
{
    /// <summary>The built-in states a custom status belongs to, as escalated-laravel lists them.</summary>
    public static readonly IReadOnlyList<string> StatusCategories = new[] { "new", "open", "pending", "on_hold", "solved" };

    private static readonly IReadOnlyList<string> PriorityValues =
        Enum.GetValues<TicketPriority>().Select(p => p.ToValue()).ToArray();

    private static readonly HashSet<string> MacroActionTypes = new() { "status", "priority", "assign", "tags", "department", "reply", "note" };

    private static Dictionary<string, string> Errors() => new();

    private string? CurrentUser() => this.CurrentUserId();

    /// <summary>A JSON array of objects from the body, as JSON text; null unless it is a non-empty array.</summary>
    private static string? JsonArray(UiInput input, string key) =>
        input.Json(key) is { ValueKind: JsonValueKind.Array } array && array.GetArrayLength() > 0 ? array.GetRawText() : null;

    // --- SLA policies ---

    [HttpGet("sla-policies")]
    [EscalatedPage(Negotiate = true)]
    public async Task<IActionResult> SlaPolicies(CancellationToken ct)
    {
        var rows = await _db.SlaPolicies.AsNoTracking().OrderBy(p => p.Name).ThenBy(p => p.Id)
            .Select(p => new { Policy = p, Tickets = _db.Tickets.IgnoreQueryFilters().Count(t => t.SlaPolicyId == p.Id && t.DeletedAt == null) })
            .ToListAsync(ct);

        return UiResults.Render("Escalated/Admin/SlaPolicies/Index", new
        {
            policies = rows.Select(row =>
            {
                var item = SlaPolicy(row.Policy);
                item["tickets_count"] = row.Tickets;
                return item;
            }).ToList(),
        });
    }

    [HttpGet("sla-policies/create")]
    public async Task<IActionResult> CreateSlaPolicy(CancellationToken ct) =>
        UiResults.Render("Escalated/Admin/SlaPolicies/Form", await SlaFormPropsAsync(null, ct));

    [HttpGet("sla-policies/{id:int}/edit")]
    public async Task<IActionResult> EditSlaPolicy(int id, CancellationToken ct)
    {
        var policy = await _db.SlaPolicies.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (policy is null) return NotFound();
        return UiResults.Render("Escalated/Admin/SlaPolicies/Form", await SlaFormPropsAsync(policy, ct));
    }

    private async Task<Dictionary<string, object?>> SlaFormPropsAsync(SlaPolicy? policy, CancellationToken ct) => new()
    {
        ["policy"] = policy is null ? null : SlaPolicy(policy),
        ["priorities"] = PriorityValues,
        ["targeted"] = true,
        ["departments"] = await _present.DepartmentOptionsAsync(ct),
        ["ticketTypes"] = Ticket.ValidTypes,
    };

    private static Dictionary<string, object?> SlaPolicy(SlaPolicy policy) => new()
    {
        ["id"] = policy.Id,
        ["name"] = policy.Name,
        ["description"] = policy.Description,
        ["department_id"] = policy.DepartmentId,
        ["ticket_type"] = policy.TicketType,
        ["first_response_hours"] = UiPresenter.ParseJson(policy.FirstResponseHours) ?? new Dictionary<string, object?>(),
        ["resolution_hours"] = UiPresenter.ParseJson(policy.ResolutionHours) ?? new Dictionary<string, object?>(),
        ["business_hours_only"] = policy.BusinessHoursOnly,
        ["is_default"] = policy.IsDefault,
        ["is_active"] = policy.IsActive,
        ["created_at"] = policy.CreatedAt,
        ["updated_at"] = policy.UpdatedAt,
    };

    [HttpPost("sla-policies")]
    [EscalatedPage(Negotiate = true)]
    public Task<IActionResult> StoreSlaPolicy(CancellationToken ct) => SaveSlaPolicyAsync(null, ct);

    [HttpPut("sla-policies/{id:int}")]
    [EscalatedPage(Negotiate = true)]
    public Task<IActionResult> UpdateSlaPolicy(int id, CancellationToken ct) => SaveSlaPolicyAsync(id, ct);

    private async Task<IActionResult> SaveSlaPolicyAsync(int? id, CancellationToken ct)
    {
        var policy = id is null ? new SlaPolicy { CreatedAt = DateTime.UtcNow } : await _db.SlaPolicies.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (policy is null) return NotFound();

        var input = await UiInput.ReadAsync(Request, ct);
        var errors = Errors();
        var name = input.String("name");
        if (name is null) errors["name"] = "The name field is required.";
        else if (name.Length > 100) errors["name"] = "The name may not be greater than 100 characters.";

        var firstResponse = Hours(input, "first_response_hours", errors);
        var resolution = Hours(input, "resolution_hours", errors);

        var departmentId = input.Int("department_id");
        if (departmentId is not null && !await _db.Departments.AnyAsync(d => d.Id == departmentId, ct))
        {
            errors["department_id"] = "The selected department is invalid.";
        }

        var ticketType = input.String("ticket_type");
        if (ticketType is not null && !Ticket.ValidTypes.Contains(ticketType)) errors["ticket_type"] = "The selected ticket type is invalid.";

        var formUrl = id is null ? $"{Prefix}/admin/sla-policies/create" : $"{Prefix}/admin/sla-policies/{id}/edit";
        if (errors.Count > 0) return UiResults.Back(HttpContext, formUrl, errors: errors);

        policy.Name = name!;
        policy.Description = input.String("description");
        policy.FirstResponseHours = JsonSerializer.Serialize(firstResponse);
        policy.ResolutionHours = JsonSerializer.Serialize(resolution);
        policy.BusinessHoursOnly = input.Bool("business_hours_only");
        policy.IsDefault = input.Bool("is_default");
        policy.IsActive = !input.Has("is_active") || input.Bool("is_active");
        policy.DepartmentId = departmentId;
        policy.TicketType = ticketType;
        policy.UpdatedAt = DateTime.UtcNow;
        if (id is null) _db.SlaPolicies.Add(policy);
        await _db.SaveChangesAsync(ct);

        return new UiRedirectResult($"{Prefix}/admin/sla-policies") { Success = id is null ? "SLA policy created." : "SLA policy updated." };
    }

    /// <summary>
    /// Per-priority hours from an object keyed by priority. Blank entries are left
    /// out (no target for that priority); the field itself is required.
    /// </summary>
    private static Dictionary<string, double> Hours(UiInput input, string key, Dictionary<string, string> errors)
    {
        var hours = new Dictionary<string, double>();
        if (input.Json(key) is not { ValueKind: JsonValueKind.Object } value)
        {
            errors[key] = $"The {key.Replace('_', ' ')} field is required.";
            return hours;
        }

        foreach (var entry in value.EnumerateObject())
        {
            if (!PriorityValues.Contains(entry.Name)) continue;
            var raw = entry.Value.ValueKind switch
            {
                JsonValueKind.Number => entry.Value.GetRawText(),
                JsonValueKind.String => entry.Value.GetString(),
                _ => null,
            };
            if (string.IsNullOrWhiteSpace(raw)) continue;
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || number < 0)
            {
                errors[key] = $"The {key.Replace('_', ' ')} must be numbers of hours.";
                continue;
            }

            hours[entry.Name] = number;
        }

        return hours;
    }

    [HttpDelete("sla-policies/{id:int}")]
    [EscalatedPage(Negotiate = true)]
    public async Task<IActionResult> DestroySlaPolicy(int id, CancellationToken ct)
    {
        var policy = await _db.SlaPolicies.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (policy is null) return NotFound();

        await _db.Tickets.IgnoreQueryFilters().Where(t => t.SlaPolicyId == id)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.SlaPolicyId, (int?)null), ct);
        _db.SlaPolicies.Remove(policy);
        await _db.SaveChangesAsync(ct);

        return new UiRedirectResult($"{Prefix}/admin/sla-policies") { Success = "SLA policy deleted." };
    }

    // --- Escalation rules ---

    [HttpGet("escalation-rules")]
    [EscalatedPage(Negotiate = true)]
    public async Task<IActionResult> EscalationRules(CancellationToken ct)
    {
        var rules = await _db.EscalationRules.AsNoTracking().OrderBy(r => r.Order).ThenBy(r => r.Id).ToListAsync(ct);
        return UiResults.Render("Escalated/Admin/EscalationRules/Index", new { rules = rules.Select(EscalationRule).ToList() });
    }

    [HttpGet("escalation-rules/create")]
    public IActionResult CreateEscalationRule() =>
        UiResults.Render("Escalated/Admin/EscalationRules/Form", new Dictionary<string, object?> { ["rule"] = null });

    [HttpGet("escalation-rules/{id:int}/edit")]
    public async Task<IActionResult> EditEscalationRule(int id, CancellationToken ct)
    {
        var rule = await _db.EscalationRules.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rule is null) return NotFound();
        return UiResults.Render("Escalated/Admin/EscalationRules/Form", new { rule = EscalationRule(rule) });
    }

    private static Dictionary<string, object?> EscalationRule(EscalationRule rule) => new()
    {
        ["id"] = rule.Id,
        ["name"] = rule.Name,
        ["description"] = rule.Description,
        ["category"] = rule.Category,
        ["trigger_type"] = rule.TriggerType,
        ["conditions"] = UiPresenter.ParseJson(rule.Conditions) ?? Array.Empty<object>(),
        ["actions"] = UiPresenter.ParseJson(rule.Actions) ?? Array.Empty<object>(),
        ["order"] = rule.Order,
        ["is_active"] = rule.IsActive,
        ["created_at"] = rule.CreatedAt,
        ["updated_at"] = rule.UpdatedAt,
    };

    [HttpPost("escalation-rules")]
    [EscalatedPage(Negotiate = true)]
    public Task<IActionResult> StoreEscalationRule(CancellationToken ct) => SaveEscalationRuleAsync(null, ct);

    [HttpPut("escalation-rules/{id:int}")]
    [EscalatedPage(Negotiate = true)]
    public Task<IActionResult> UpdateEscalationRule(int id, CancellationToken ct) => SaveEscalationRuleAsync(id, ct);

    private async Task<IActionResult> SaveEscalationRuleAsync(int? id, CancellationToken ct)
    {
        var rule = id is null ? new EscalationRule { CreatedAt = DateTime.UtcNow } : await _db.EscalationRules.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rule is null) return NotFound();

        var input = await UiInput.ReadAsync(Request, ct);
        var errors = Errors();
        var name = input.String("name");
        if (name is null) errors["name"] = "The name field is required.";
        else if (name.Length > 100) errors["name"] = "The name may not be greater than 100 characters.";
        var trigger = input.String("trigger_type");
        if (trigger is null) errors["trigger_type"] = "The trigger type field is required.";
        else if (trigger.Length > 50) errors["trigger_type"] = "The trigger type may not be greater than 50 characters.";
        var conditions = JsonArray(input, "conditions");
        if (conditions is null) errors["conditions"] = "The conditions field is required.";
        var actions = JsonArray(input, "actions");
        if (actions is null) errors["actions"] = "The actions field is required.";
        if (input.Has("order") && input.String("order") is not null && input.Int("order") is null) errors["order"] = "The order must be an integer.";

        var formUrl = id is null ? $"{Prefix}/admin/escalation-rules/create" : $"{Prefix}/admin/escalation-rules/{id}/edit";
        if (errors.Count > 0) return UiResults.Back(HttpContext, formUrl, errors: errors);

        rule.Name = name!;
        rule.Description = input.String("description");
        rule.TriggerType = trigger!;
        rule.Conditions = conditions!;
        rule.Actions = actions!;
        rule.Order = input.Int("order") ?? 0;
        rule.IsActive = !input.Has("is_active") || input.Bool("is_active");
        rule.UpdatedAt = DateTime.UtcNow;
        if (id is null) _db.EscalationRules.Add(rule);
        await _db.SaveChangesAsync(ct);

        return new UiRedirectResult($"{Prefix}/admin/escalation-rules") { Success = id is null ? "Escalation rule created." : "Escalation rule updated." };
    }

    [HttpDelete("escalation-rules/{id:int}")]
    [EscalatedPage(Negotiate = true)]
    public async Task<IActionResult> DestroyEscalationRule(int id, CancellationToken ct)
    {
        var rule = await _db.EscalationRules.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rule is null) return NotFound();

        _db.EscalationRules.Remove(rule);
        await _db.SaveChangesAsync(ct);
        return new UiRedirectResult($"{Prefix}/admin/escalation-rules") { Success = "Escalation rule deleted." };
    }

    // --- Canned responses ---

    [HttpGet("canned-responses")]
    [EscalatedPage(Negotiate = true)]
    public async Task<IActionResult> CannedResponses(CancellationToken ct)
    {
        var responses = await _db.CannedResponses.AsNoTracking().OrderBy(c => c.Title).ThenBy(c => c.Id).ToListAsync(ct);
        var rows = new List<Dictionary<string, object?>>();
        foreach (var response in responses)
        {
            rows.Add(new Dictionary<string, object?>
            {
                ["id"] = response.Id,
                ["title"] = response.Title,
                ["body"] = response.Body,
                ["category"] = response.Category,
                ["is_shared"] = response.IsShared,
                ["created_by"] = response.CreatedBy,
                ["creator"] = await _present.UserAsync(response.CreatedBy, ct),
                ["created_at"] = response.CreatedAt,
                ["updated_at"] = response.UpdatedAt,
            });
        }

        return UiResults.Render("Escalated/Admin/CannedResponses/Index", new { responses = rows });
    }

    [HttpPost("canned-responses")]
    [EscalatedPage(Negotiate = true)]
    public Task<IActionResult> StoreCannedResponse(CancellationToken ct) => SaveCannedResponseAsync(null, ct);

    [HttpPut("canned-responses/{id:int}")]
    [EscalatedPage(Negotiate = true)]
    public Task<IActionResult> UpdateCannedResponse(int id, CancellationToken ct) => SaveCannedResponseAsync(id, ct);

    private async Task<IActionResult> SaveCannedResponseAsync(int? id, CancellationToken ct)
    {
        var response = id is null
            ? new CannedResponse { CreatedAt = DateTime.UtcNow, CreatedBy = CurrentUser() }
            : await _db.CannedResponses.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (response is null) return NotFound();

        var input = await UiInput.ReadAsync(Request, ct);
        var errors = Errors();
        var title = input.String("title");
        var body = input.String("body");
        var category = input.String("category");
        if (title is null) errors["title"] = "The title field is required.";
        else if (title.Length > 100) errors["title"] = "The title may not be greater than 100 characters.";
        if (body is null) errors["body"] = "The body field is required.";
        if (category is { Length: > 50 }) errors["category"] = "The category may not be greater than 50 characters.";

        var back = $"{Prefix}/admin/canned-responses";
        if (errors.Count > 0) return UiResults.Back(HttpContext, back, errors: errors);

        response.Title = title!;
        response.Body = body!;
        response.Category = category;
        response.IsShared = !input.Has("is_shared") || input.Bool("is_shared");
        response.UpdatedAt = DateTime.UtcNow;
        if (id is null) _db.CannedResponses.Add(response);
        await _db.SaveChangesAsync(ct);

        return UiResults.Back(HttpContext, back, success: id is null ? "Canned response created." : "Canned response updated.");
    }

    [HttpDelete("canned-responses/{id:int}")]
    [EscalatedPage(Negotiate = true)]
    public async Task<IActionResult> DestroyCannedResponse(int id, CancellationToken ct)
    {
        var response = await _db.CannedResponses.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (response is null) return NotFound();

        _db.CannedResponses.Remove(response);
        await _db.SaveChangesAsync(ct);
        return UiResults.Back(HttpContext, $"{Prefix}/admin/canned-responses", success: "Canned response deleted.");
    }

    // --- Macros ---

    [HttpGet("macros")]
    [EscalatedPage(Negotiate = true)]
    public async Task<IActionResult> Macros(CancellationToken ct)
    {
        var macros = await _db.Macros.AsNoTracking().OrderBy(m => m.Order).ThenBy(m => m.Id).ToListAsync(ct);
        var rows = new List<Dictionary<string, object?>>();
        foreach (var macro in macros)
        {
            rows.Add(new Dictionary<string, object?>
            {
                ["id"] = macro.Id,
                ["name"] = macro.Name,
                ["description"] = macro.Description,
                ["actions"] = UiPresenter.ParseJson(macro.Actions) ?? Array.Empty<object>(),
                ["is_shared"] = macro.IsShared,
                ["order"] = macro.Order,
                ["created_by"] = macro.CreatedBy,
                ["creator"] = await _present.UserAsync(macro.CreatedBy, ct),
                ["created_at"] = macro.CreatedAt,
                ["updated_at"] = macro.UpdatedAt,
            });
        }

        return UiResults.Render("Escalated/Admin/Macros/Index", new { macros = rows });
    }

    [HttpPost("macros")]
    [EscalatedPage(Negotiate = true)]
    public Task<IActionResult> StoreMacro(CancellationToken ct) => SaveMacroAsync(null, ct);

    [HttpPut("macros/{id:int}")]
    [EscalatedPage(Negotiate = true)]
    public Task<IActionResult> UpdateMacro(int id, CancellationToken ct) => SaveMacroAsync(id, ct);

    private async Task<IActionResult> SaveMacroAsync(int? id, CancellationToken ct)
    {
        var macro = id is null
            ? new Macro { CreatedAt = DateTime.UtcNow, CreatedBy = CurrentUser() }
            : await _db.Macros.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (macro is null) return NotFound();

        var input = await UiInput.ReadAsync(Request, ct);
        var errors = Errors();
        var name = input.String("name");
        var description = input.String("description");
        if (name is null) errors["name"] = "The name field is required.";
        else if (name.Length > 100) errors["name"] = "The name may not be greater than 100 characters.";
        if (description is { Length: > 500 }) errors["description"] = "The description may not be greater than 500 characters.";
        if (input.Has("order") && input.String("order") is not null && input.Int("order") is not >= 0) errors["order"] = "The order must be a positive integer.";

        var actions = new List<Dictionary<string, object?>>();
        if (input.Json("actions") is not { ValueKind: JsonValueKind.Array } list || list.GetArrayLength() == 0)
        {
            errors["actions"] = "The actions field must have at least 1 item.";
        }
        else
        {
            var index = 0;
            foreach (var item in list.EnumerateArray())
            {
                var type = item.ValueKind == JsonValueKind.Object && item.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
                var hasValue = item.ValueKind == JsonValueKind.Object && item.TryGetProperty("value", out var v)
                    && v.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
                    && !(v.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(v.GetString()))
                    && !(v.ValueKind == JsonValueKind.Array && v.GetArrayLength() == 0);
                if (type is null || !MacroActionTypes.Contains(type))
                {
                    errors["actions"] = "Each action needs a type: status, priority, assign, tags, department, reply or note.";
                    break;
                }

                if (!hasValue)
                {
                    errors["actions"] = "Each action needs a value.";
                    break;
                }

                actions.Add(new Dictionary<string, object?>
                {
                    ["type"] = type,
                    ["value"] = item.GetProperty("value").Clone(),
                    ["order"] = item.TryGetProperty("order", out var o) && o.TryGetInt32(out var order) ? order : index,
                });
                index++;
            }
        }

        var back = $"{Prefix}/admin/macros";
        if (errors.Count > 0) return UiResults.Back(HttpContext, back, errors: errors);

        macro.Name = name!;
        macro.Description = description;
        macro.Actions = JsonSerializer.Serialize(actions);
        macro.IsShared = input.Bool("is_shared");
        if (input.Int("order") is { } macroOrder) macro.Order = macroOrder;
        macro.UpdatedAt = DateTime.UtcNow;
        if (id is null) _db.Macros.Add(macro);
        await _db.SaveChangesAsync(ct);

        return UiResults.Back(HttpContext, back, success: id is null ? "Macro created." : "Macro updated.");
    }

    [HttpDelete("macros/{id:int}")]
    [EscalatedPage(Negotiate = true)]
    public async Task<IActionResult> DestroyMacro(int id, CancellationToken ct)
    {
        var macro = await _db.Macros.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (macro is null) return NotFound();

        _db.Macros.Remove(macro);
        await _db.SaveChangesAsync(ct);
        return UiResults.Back(HttpContext, $"{Prefix}/admin/macros", success: "Macro deleted.");
    }

    // --- Statuses ---

    [HttpGet("statuses")]
    public async Task<IActionResult> Statuses(CancellationToken ct)
    {
        var statuses = await _db.TicketStatuses.AsNoTracking().ToListAsync(ct);
        return UiResults.Render("Escalated/Admin/Statuses/Index", new
        {
            statuses = statuses
                .OrderBy(s => IndexOfCategory(s.Category)).ThenBy(s => s.Position).ThenBy(s => s.Id)
                .Select(Status).ToList(),
            categories = StatusCategories,
        });
    }

    private static int IndexOfCategory(string category)
    {
        var index = StatusCategories.ToList().IndexOf(category);
        return index < 0 ? int.MaxValue : index;
    }

    [HttpGet("statuses/create")]
    public IActionResult CreateStatus() =>
        UiResults.Render("Escalated/Admin/Statuses/Form", new Dictionary<string, object?> { ["status"] = null, ["categories"] = StatusCategories });

    [HttpGet("statuses/{id:int}/edit")]
    public async Task<IActionResult> EditStatus(int id, CancellationToken ct)
    {
        var status = await _db.TicketStatuses.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (status is null) return NotFound();
        return UiResults.Render("Escalated/Admin/Statuses/Form", new { status = Status(status), categories = StatusCategories });
    }

    private static Dictionary<string, object?> Status(TicketStatusModel status) => new()
    {
        ["id"] = status.Id,
        ["label"] = status.Label,
        ["slug"] = status.Slug,
        ["category"] = status.Category,
        ["color"] = status.Color,
        ["description"] = status.Description,
        ["position"] = status.Position,
        ["is_default"] = status.IsDefault,
        ["created_at"] = status.CreatedAt,
        ["updated_at"] = status.UpdatedAt,
    };

    [HttpPost("statuses")]
    public Task<IActionResult> StoreStatus(CancellationToken ct) => SaveStatusAsync(null, ct);

    [HttpPut("statuses/{id:int}")]
    public Task<IActionResult> UpdateStatus(int id, CancellationToken ct) => SaveStatusAsync(id, ct);

    private async Task<IActionResult> SaveStatusAsync(int? id, CancellationToken ct)
    {
        var status = id is null ? new TicketStatusModel { CreatedAt = DateTime.UtcNow } : await _db.TicketStatuses.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (status is null) return NotFound();

        var input = await UiInput.ReadAsync(Request, ct);
        var errors = Errors();
        var label = input.String("label");
        var category = input.String("category");
        var color = input.String("color");
        if (label is null) errors["label"] = "The label field is required.";
        else if (label.Length > 100) errors["label"] = "The label may not be greater than 100 characters.";
        if (category is null || !StatusCategories.Contains(category)) errors["category"] = "The selected category is invalid.";
        if (color is null) errors["color"] = "The color field is required.";
        else if (color.Length > 20) errors["color"] = "The color may not be greater than 20 characters.";
        if (input.String("position") is not null && input.Int("position") is null) errors["position"] = "The position must be an integer.";

        var formUrl = id is null ? $"{Prefix}/admin/statuses/create" : $"{Prefix}/admin/statuses/{id}/edit";
        if (errors.Count > 0) return UiResults.Back(HttpContext, formUrl, errors: errors);

        var isDefault = input.Bool("is_default");
        if (isDefault)
        {
            await _db.TicketStatuses.Where(s => s.Category == category && s.Id != status.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.IsDefault, false), ct);
        }

        if (id is null)
        {
            var baseSlug = Department.GenerateSlug(label!);
            var slug = baseSlug;
            for (var n = 2; await _db.TicketStatuses.AnyAsync(s => s.Slug == slug, ct); n++)
            {
                slug = $"{baseSlug}-{n}";
            }
            status.Slug = slug;
        }

        status.Label = label!;
        status.Category = category!;
        status.Color = color;
        status.Description = input.String("description");
        status.Position = input.Int("position") ?? 0;
        status.IsDefault = isDefault;
        status.UpdatedAt = DateTime.UtcNow;
        if (id is null) _db.TicketStatuses.Add(status);
        await _db.SaveChangesAsync(ct);

        return new UiRedirectResult($"{Prefix}/admin/statuses") { Success = id is null ? "Status created." : "Status updated." };
    }

    [HttpDelete("statuses/{id:int}")]
    public async Task<IActionResult> DestroyStatus(int id, CancellationToken ct)
    {
        var status = await _db.TicketStatuses.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (status is null) return NotFound();

        _db.TicketStatuses.Remove(status);
        await _db.SaveChangesAsync(ct);
        return new UiRedirectResult($"{Prefix}/admin/statuses") { Success = "Status deleted." };
    }

    // --- Webhooks ---

    [HttpGet("webhooks")]
    [EscalatedPage(Negotiate = true)]
    public async Task<IActionResult> Webhooks(CancellationToken ct)
    {
        var webhooks = await _db.Webhooks.AsNoTracking().OrderByDescending(w => w.CreatedAt).ThenByDescending(w => w.Id)
            .Select(w => new
            {
                Webhook = w,
                Count = _db.WebhookDeliveries.Count(d => d.WebhookId == w.Id),
                Latest = _db.WebhookDeliveries.Where(d => d.WebhookId == w.Id)
                    .OrderByDescending(d => d.CreatedAt).ThenByDescending(d => d.Id).FirstOrDefault(),
            })
            .ToListAsync(ct);

        return UiResults.Render("Escalated/Admin/Webhooks/Index", new
        {
            webhooks = webhooks.Select(row =>
            {
                var item = Webhook(row.Webhook);
                item["deliveries_count"] = row.Count;
                item["deliveries"] = row.Latest is null ? new List<object?>() : new List<object?> { Delivery(row.Latest) };
                return item;
            }).ToList(),
        });
    }

    [HttpGet("webhooks/create")]
    public IActionResult CreateWebhook() =>
        UiResults.Render("Escalated/Admin/Webhooks/Form", new Dictionary<string, object?> { ["webhook"] = null, ["availableEvents"] = EventNames.Canonical });

    [HttpGet("webhooks/{id:int}/edit")]
    public async Task<IActionResult> EditWebhook(int id, CancellationToken ct)
    {
        var webhook = await _db.Webhooks.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id, ct);
        if (webhook is null) return NotFound();

        var item = Webhook(webhook);
        item["secret"] = webhook.Secret;
        return UiResults.Render("Escalated/Admin/Webhooks/Form", new { webhook = item, availableEvents = EventNames.Canonical });
    }

    [HttpGet("webhooks/{id:int}/deliveries")]
    public async Task<IActionResult> WebhookDeliveries(int id, CancellationToken ct)
    {
        var webhook = await _db.Webhooks.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id, ct);
        if (webhook is null) return NotFound();

        const int perPage = 25;
        var page = Math.Max(1, int.TryParse(Request.Query["page"], out var p) ? p : 1);
        var query = _db.WebhookDeliveries.AsNoTracking().Where(d => d.WebhookId == id);
        var total = await query.CountAsync(ct);
        var deliveries = await query.OrderByDescending(d => d.CreatedAt).ThenByDescending(d => d.Id)
            .Skip((page - 1) * perPage).Take(perPage).ToListAsync(ct);

        return UiResults.Render("Escalated/Admin/Webhooks/DeliveryLog", new
        {
            webhook = Webhook(webhook),
            deliveries = UiPresenter.Paginator(deliveries.Select(d => (object?)Delivery(d)).ToList(), total, page, perPage,
                Request.Path, new Dictionary<string, string?>()),
        });
    }

    /// <summary>A webhook without its secret, which only the edit form shows.</summary>
    private static Dictionary<string, object?> Webhook(Webhook webhook) => new()
    {
        ["id"] = webhook.Id,
        ["url"] = webhook.Url,
        ["description"] = webhook.Description,
        ["events"] = SubscribedEvents(webhook.Events),
        ["active"] = webhook.Active,
        ["created_at"] = webhook.CreatedAt,
        ["updated_at"] = webhook.UpdatedAt,
    };

    /// <summary>The subscription as canonical names, so the form ticks one saved under a legacy name.</summary>
    private static List<string> SubscribedEvents(string events)
    {
        try
        {
            return (JsonSerializer.Deserialize<List<string>>(events) ?? new List<string>())
                .Select(EventNames.Canonicalize).Distinct().ToList();
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }

    private static Dictionary<string, object?> Delivery(WebhookDelivery delivery) => new()
    {
        ["id"] = delivery.Id,
        ["webhook_id"] = delivery.WebhookId,
        ["event"] = delivery.Event,
        ["payload"] = UiPresenter.ParseJson(delivery.Payload),
        ["response_code"] = delivery.ResponseCode,
        ["response_body"] = delivery.ResponseBody,
        ["attempts"] = delivery.Attempts,
        ["delivered_at"] = delivery.DeliveredAt,
        ["created_at"] = delivery.CreatedAt,
    };

    [HttpPost("webhooks")]
    [EscalatedPage(Negotiate = true)]
    public Task<IActionResult> StoreWebhook(CancellationToken ct) => SaveWebhookAsync(null, ct);

    [HttpPut("webhooks/{id:int}")]
    public Task<IActionResult> UpdateWebhook(int id, CancellationToken ct) => SaveWebhookAsync(id, ct);

    private async Task<IActionResult> SaveWebhookAsync(int? id, CancellationToken ct)
    {
        var webhook = id is null ? new Webhook { CreatedAt = DateTime.UtcNow } : await _db.Webhooks.FirstOrDefaultAsync(w => w.Id == id, ct);
        if (webhook is null) return NotFound();

        var input = await UiInput.ReadAsync(Request, ct);
        var errors = Errors();
        var url = input.String("url");
        if (url is null) errors["url"] = "The url field is required.";
        else if (url.Length > 500) errors["url"] = "The url may not be greater than 500 characters.";
        else if (!WebhookDispatcher.IsSafeWebhookUrl(url)) errors["url"] = "The webhook URL must use http or https and resolve to a public address.";
        var events = input.List("events");
        if (events.Count == 0) errors["events"] = "The events field must have at least 1 item.";
        var secret = input.String("secret");
        if (secret is { Length: > 255 }) errors["secret"] = "The secret may not be greater than 255 characters.";

        var formUrl = id is null ? $"{Prefix}/admin/webhooks/create" : $"{Prefix}/admin/webhooks/{id}/edit";
        if (errors.Count > 0) return UiResults.Back(HttpContext, formUrl, errors: errors);

        webhook.Url = url!;
        webhook.Events = JsonSerializer.Serialize(events.Select(EventNames.Canonicalize).Distinct().ToList());
        webhook.Secret = secret;
        webhook.Active = !input.Has("active") || input.Bool("active");
        webhook.UpdatedAt = DateTime.UtcNow;
        if (id is null) _db.Webhooks.Add(webhook);
        await _db.SaveChangesAsync(ct);

        return new UiRedirectResult($"{Prefix}/admin/webhooks") { Success = id is null ? "Webhook created." : "Webhook updated." };
    }

    [HttpDelete("webhooks/{id:int}")]
    public async Task<IActionResult> DestroyWebhook(int id, CancellationToken ct)
    {
        var webhook = await _db.Webhooks.FirstOrDefaultAsync(w => w.Id == id, ct);
        if (webhook is null) return NotFound();

        await _db.WebhookDeliveries.Where(d => d.WebhookId == id).ExecuteDeleteAsync(ct);
        _db.Webhooks.Remove(webhook);
        await _db.SaveChangesAsync(ct);
        return new UiRedirectResult($"{Prefix}/admin/webhooks") { Success = "Webhook deleted." };
    }

    /// <summary>Sends the delivery's event and payload to its webhook again, recording a new delivery.</summary>
    [HttpPost("webhooks/deliveries/{id:int}/retry")]
    public async Task<IActionResult> RetryWebhookDelivery(int id, [FromServices] WebhookDispatcher dispatcher, CancellationToken ct)
    {
        var delivery = await _db.WebhookDeliveries.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);
        if (delivery is null) return NotFound();

        await dispatcher.RetryDeliveryAsync(delivery, ct);
        return UiResults.Back(HttpContext, $"{Prefix}/admin/webhooks/{delivery.WebhookId}/deliveries", success: "Webhook delivery retried.");
    }
}
