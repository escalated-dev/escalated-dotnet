using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Escalated.Models;
using Escalated.Ui;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Controllers.Ui;

/// <summary>
/// The admin operations pages, with escalated-laravel's routes and props:
/// <c>Automations/Index</c> and <c>Form</c> (<c>AutomationController</c>),
/// <c>BusinessHours/Index</c> and <c>Form</c> (<c>BusinessHoursController</c>),
/// <c>Skills/Index</c> and <c>Form</c> (<c>SkillController</c>),
/// <c>Capacity/Index</c> (<c>CapacityController</c>) and <c>AuditLog/Index</c>
/// (<c>AuditLogController</c>). The routes the JSON admin API also answers are
/// negotiated, so API clients are unchanged.
/// </summary>
public partial class AdminPageController
{
    private static readonly string[] Weekdays = { "monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday" };

    /// <summary>The actions the audit log screen filters by, as escalated-laravel lists them.</summary>
    public static readonly IReadOnlyList<string> AuditActions = new[] { "created", "updated", "deleted" };

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}$")]
    private static partial Regex IsoDate();

    /// <summary>A JSON array of objects from the body, or null unless it is a non-empty one.</summary>
    private static List<JsonElement>? ObjectArray(UiInput input, string key) =>
        input.Json(key) is { ValueKind: JsonValueKind.Array } array && array.GetArrayLength() > 0
            && array.EnumerateArray().All(item => item.ValueKind == JsonValueKind.Object)
            ? array.EnumerateArray().Select(item => item.Clone()).ToList()
            : null;

    private static string? RequiredName(UiInput input, int max, Dictionary<string, string> errors)
    {
        var name = input.String("name")?.Trim();
        if (string.IsNullOrEmpty(name)) errors["name"] = "The name field is required.";
        else if (name.Length > max) errors["name"] = $"The name may not be greater than {max} characters.";
        return name;
    }

    // --- Automations ---

    private static Dictionary<string, object?> Automation(Automation automation) => new()
    {
        ["id"] = automation.Id,
        ["name"] = automation.Name,
        ["description"] = automation.Description,
        ["conditions"] = UiPresenter.ParseJson(automation.Conditions) ?? Array.Empty<object>(),
        ["actions"] = UiPresenter.ParseJson(automation.Actions) ?? Array.Empty<object>(),
        ["active"] = automation.Active,
        ["position"] = automation.Position,
        ["last_run_at"] = automation.LastRunAt,
        ["created_at"] = automation.CreatedAt,
        ["updated_at"] = automation.UpdatedAt,
    };

    [HttpGet("automations")]
    [EscalatedPage(Negotiate = true)]
    public async Task<IActionResult> Automations(CancellationToken ct)
    {
        var rows = await _db.Automations.AsNoTracking().OrderBy(a => a.Position).ThenBy(a => a.Id).ToListAsync(ct);
        return UiResults.Render("Escalated/Admin/Automations/Index", new { automations = rows.Select(Automation).ToList() });
    }

    [HttpGet("automations/create")]
    public IActionResult CreateAutomation() =>
        UiResults.Render("Escalated/Admin/Automations/Form", new Dictionary<string, object?> { ["automation"] = null });

    [HttpGet("automations/{id:int}/edit")]
    public async Task<IActionResult> EditAutomation(int id, CancellationToken ct)
    {
        var automation = await _db.Automations.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (automation is null) return NotFound();
        return UiResults.Render("Escalated/Admin/Automations/Form", new { automation = Automation(automation) });
    }

    [HttpPost("automations")]
    [EscalatedPage(Negotiate = true)]
    public Task<IActionResult> StoreAutomation(CancellationToken ct) => SaveAutomationAsync(null, ct);

    [HttpPut("automations/{id:int}")]
    public Task<IActionResult> UpdateAutomation(int id, CancellationToken ct) => SaveAutomationAsync(id, ct);

    private async Task<IActionResult> SaveAutomationAsync(int? id, CancellationToken ct)
    {
        var automation = id is null ? new Automation { CreatedAt = DateTime.UtcNow } : await _db.Automations.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (automation is null) return NotFound();

        var input = await UiInput.ReadAsync(Request, ct);
        var errors = Errors();
        var name = RequiredName(input, 100, errors);
        var conditions = ObjectArray(input, "conditions");
        if (conditions is null) errors["conditions"] = "The conditions field must have at least 1 item.";
        var actions = ObjectArray(input, "actions");
        if (actions is null) errors["actions"] = "The actions field must have at least 1 item.";

        var formUrl = id is null ? $"{Prefix}/admin/automations/create" : $"{Prefix}/admin/automations/{id}/edit";
        if (errors.Count > 0) return UiResults.Back(HttpContext, formUrl, errors: errors);

        automation.Name = name!;
        automation.Conditions = JsonSerializer.Serialize(conditions);
        automation.Actions = JsonSerializer.Serialize(actions);
        automation.Active = !input.Has("active") || input.Bool("active");
        automation.UpdatedAt = DateTime.UtcNow;
        if (id is null)
        {
            automation.Position = (await _db.Automations.MaxAsync(a => (int?)a.Position, ct) ?? 0) + 1;
            _db.Automations.Add(automation);
        }

        await _db.SaveChangesAsync(ct);
        return new UiRedirectResult($"{Prefix}/admin/automations") { Success = id is null ? "Automation created." : "Automation updated." };
    }

    [HttpDelete("automations/{id:int}")]
    public async Task<IActionResult> DestroyAutomation(int id, CancellationToken ct)
    {
        var automation = await _db.Automations.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (automation is null) return NotFound();

        _db.Automations.Remove(automation);
        await _db.SaveChangesAsync(ct);
        return new UiRedirectResult($"{Prefix}/admin/automations") { Success = "Automation deleted." };
    }

    // --- Business hours ---

    /// <summary>Every IANA zone this runtime knows, as PHP's <c>timezone_identifiers_list()</c>.</summary>
    public static IReadOnlyList<string> Timezones()
    {
        var zones = new SortedSet<string>(StringComparer.Ordinal) { "UTC" };
        foreach (var zone in TimeZoneInfo.GetSystemTimeZones())
        {
            if (zone.HasIanaId) zones.Add(zone.Id);
            else if (TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var iana)) zones.Add(iana);
        }

        return zones.ToList();
    }

    private static bool IsTimezone(string? zone)
    {
        if (string.IsNullOrEmpty(zone)) return false;
        if (zone == "UTC") return true;
        return TimeZoneInfo.TryConvertIanaIdToWindowsId(zone, out _) && TimeZoneInfo.TryFindSystemTimeZoneById(zone, out _);
    }

    private static Dictionary<string, object?> Holiday(Holiday holiday) => new()
    {
        ["id"] = holiday.Id,
        ["schedule_id"] = holiday.ScheduleId,
        ["name"] = holiday.Name,
        ["date"] = holiday.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ["recurring"] = holiday.Recurring,
        ["created_at"] = holiday.CreatedAt,
    };

    private static Dictionary<string, object?> Schedule(BusinessSchedule schedule) => new()
    {
        ["id"] = schedule.Id,
        ["name"] = schedule.Name,
        ["timezone"] = schedule.Timezone,
        ["is_default"] = schedule.IsDefault,
        ["schedule"] = UiPresenter.ParseJson(schedule.Schedule) ?? new Dictionary<string, object?>(),
        ["holidays"] = schedule.Holidays.OrderBy(h => h.Date).ThenBy(h => h.Id).Select(Holiday).ToList(),
        ["created_at"] = schedule.CreatedAt,
        ["updated_at"] = schedule.UpdatedAt,
    };

    [HttpGet("business-hours")]
    [EscalatedPage(Negotiate = true)]
    public async Task<IActionResult> BusinessHours(CancellationToken ct)
    {
        var rows = await _db.BusinessSchedules.AsNoTracking().Include(s => s.Holidays).OrderBy(s => s.Id).ToListAsync(ct);
        return UiResults.Render("Escalated/Admin/BusinessHours/Index", new { schedules = rows.Select(Schedule).ToList() });
    }

    [HttpGet("business-hours/create")]
    public IActionResult CreateBusinessHours() =>
        UiResults.Render("Escalated/Admin/BusinessHours/Form", new Dictionary<string, object?>
        {
            ["schedule"] = null,
            ["timezones"] = Timezones(),
        });

    [HttpGet("business-hours/{id:int}/edit")]
    public async Task<IActionResult> EditBusinessHours(int id, CancellationToken ct)
    {
        var schedule = await _db.BusinessSchedules.AsNoTracking().Include(s => s.Holidays).FirstOrDefaultAsync(s => s.Id == id, ct);
        if (schedule is null) return NotFound();
        return UiResults.Render("Escalated/Admin/BusinessHours/Form", new { schedule = Schedule(schedule), timezones = Timezones() });
    }

    [HttpPost("business-hours")]
    [EscalatedPage(Negotiate = true)]
    public Task<IActionResult> StoreBusinessHours(CancellationToken ct) => SaveBusinessHoursAsync(null, ct);

    [HttpPut("business-hours/{id:int}")]
    public Task<IActionResult> UpdateBusinessHours(int id, CancellationToken ct) => SaveBusinessHoursAsync(id, ct);

    private async Task<IActionResult> SaveBusinessHoursAsync(int? id, CancellationToken ct)
    {
        var schedule = id is null
            ? new BusinessSchedule { CreatedAt = DateTime.UtcNow }
            : await _db.BusinessSchedules.Include(s => s.Holidays).FirstOrDefaultAsync(s => s.Id == id, ct);
        if (schedule is null) return NotFound();

        var input = await UiInput.ReadAsync(Request, ct);
        var errors = Errors();
        var name = RequiredName(input, 100, errors);
        var timezone = input.String("timezone");
        if (!IsTimezone(timezone)) errors["timezone"] = "The timezone must be a valid timezone.";

        // The weekly hours, as the schedule editor sends them: `{ day: { start, end } }`.
        // A closed day (no start or end) is left out, which is how the calculator reads it.
        var week = new Dictionary<string, Dictionary<string, string>>();
        if (input.Json("schedule") is not { ValueKind: JsonValueKind.Object } days)
        {
            errors["schedule"] = "The schedule field is required.";
        }
        else
        {
            foreach (var day in Weekdays)
            {
                if (!days.TryGetProperty(day, out var hours) || hours.ValueKind != JsonValueKind.Object) continue;
                var start = hours.TryGetProperty("start", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
                var end = hours.TryGetProperty("end", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
                if (!string.IsNullOrEmpty(start) && !string.IsNullOrEmpty(end))
                {
                    week[day] = new Dictionary<string, string> { ["start"] = start, ["end"] = end };
                }
            }
        }

        var holidays = new List<Holiday>();
        var rawHolidays = input.Json("holidays");
        if (rawHolidays is { ValueKind: JsonValueKind.Array } list)
        {
            var index = 0;
            foreach (var item in list.EnumerateArray())
            {
                var holidayName = item.ValueKind == JsonValueKind.Object && item.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()?.Trim() : null;
                if (string.IsNullOrEmpty(holidayName) || holidayName.Length > 100) errors[$"holidays.{index}.name"] = "The holiday name field is required.";

                var rawDate = item.ValueKind == JsonValueKind.Object && item.TryGetProperty("date", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
                var dateText = rawDate is { Length: >= 10 } ? rawDate[..10] : rawDate;
                DateTime date = default;
                if (dateText is null || !IsoDate().IsMatch(dateText)
                    || !DateTime.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out date))
                {
                    errors[$"holidays.{index}.date"] = "The holiday date must be a valid date.";
                }

                var recurring = item.ValueKind == JsonValueKind.Object && item.TryGetProperty("recurring", out var r)
                    && (r.ValueKind == JsonValueKind.True || (r.ValueKind == JsonValueKind.String && r.GetString() is "1" or "true"));
                holidays.Add(new Holiday { Name = holidayName ?? string.Empty, Date = date, Recurring = recurring, CreatedAt = DateTime.UtcNow });
                index++;
            }
        }
        else if (rawHolidays is not null && rawHolidays.Value.ValueKind is not JsonValueKind.Null)
        {
            errors["holidays"] = "The holidays must be a list.";
        }

        var formUrl = id is null ? $"{Prefix}/admin/business-hours/create" : $"{Prefix}/admin/business-hours/{id}/edit";
        if (errors.Count > 0) return UiResults.Back(HttpContext, formUrl, errors: errors);

        schedule.Name = name!;
        schedule.Timezone = timezone!;
        schedule.IsDefault = input.Bool("is_default");
        schedule.Schedule = JsonSerializer.Serialize(week);
        schedule.UpdatedAt = DateTime.UtcNow;
        _db.Holidays.RemoveRange(schedule.Holidays);
        schedule.Holidays.Clear();
        foreach (var holiday in holidays)
        {
            schedule.Holidays.Add(holiday);
        }

        if (id is null) _db.BusinessSchedules.Add(schedule);
        await _db.SaveChangesAsync(ct);

        if (schedule.IsDefault)
        {
            await _db.BusinessSchedules.Where(s => s.IsDefault && s.Id != schedule.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDefault, false), ct);
        }

        return new UiRedirectResult($"{Prefix}/admin/business-hours") { Success = id is null ? "Business schedule created." : "Business schedule updated." };
    }

    [HttpDelete("business-hours/{id:int}")]
    public async Task<IActionResult> DestroyBusinessHours(int id, CancellationToken ct)
    {
        var schedule = await _db.BusinessSchedules.Include(s => s.Holidays).FirstOrDefaultAsync(s => s.Id == id, ct);
        if (schedule is null) return NotFound();

        _db.Holidays.RemoveRange(schedule.Holidays);
        _db.BusinessSchedules.Remove(schedule);
        await _db.SaveChangesAsync(ct);
        return new UiRedirectResult($"{Prefix}/admin/business-hours") { Success = "Business schedule deleted." };
    }

    // --- Skills ---

    private async Task<Dictionary<string, object?>> SkillFormPropsAsync(Dictionary<string, object?>? skill, CancellationToken ct) => new()
    {
        ["skill"] = skill,
        ["availableAgents"] = await _present.AgentOptionsAsync(ct),
        ["availableTags"] = await _present.TagOptionsAsync(ct),
        ["availableDepartments"] = (await _db.Departments.AsNoTracking().OrderBy(d => d.Name).ToListAsync(ct))
            .Select(d => new Dictionary<string, object?> { ["id"] = d.Id, ["name"] = d.Name }).ToList(),
    };

    [HttpGet("skills")]
    [EscalatedPage(Negotiate = true)]
    public async Task<IActionResult> Skills(CancellationToken ct)
    {
        var rows = await _db.Skills.AsNoTracking().OrderBy(s => s.Name)
            .Select(s => new
            {
                s.Id,
                s.Name,
                s.Slug,
                Agents = s.AgentSkills.Count,
                Tags = s.RoutingTags.Count,
                Departments = s.RoutingDepartments.Count,
                s.UpdatedAt,
            })
            .ToListAsync(ct);

        return UiResults.Render("Escalated/Admin/Skills/Index", new
        {
            skills = rows.Select(s => new Dictionary<string, object?>
            {
                ["id"] = s.Id,
                ["name"] = s.Name,
                ["slug"] = s.Slug,
                ["agents_count"] = s.Agents,
                ["routing_tags_count"] = s.Tags,
                ["routing_departments_count"] = s.Departments,
                ["updated_at"] = s.UpdatedAt,
            }).ToList(),
        });
    }

    [HttpGet("skills/create")]
    [EscalatedPage(Negotiate = true)]
    public async Task<IActionResult> CreateSkill(CancellationToken ct) =>
        UiResults.Render("Escalated/Admin/Skills/Form", await SkillFormPropsAsync(null, ct));

    [HttpGet("skills/{id:int}/edit")]
    [EscalatedPage(Negotiate = true)]
    public async Task<IActionResult> EditSkill(int id, CancellationToken ct)
    {
        var skill = await _db.Skills.AsNoTracking()
            .Include(s => s.AgentSkills).Include(s => s.RoutingTags).Include(s => s.RoutingDepartments)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
        if (skill is null) return NotFound();

        return UiResults.Render("Escalated/Admin/Skills/Form", await SkillFormPropsAsync(new Dictionary<string, object?>
        {
            ["id"] = skill.Id,
            ["name"] = skill.Name,
            ["slug"] = skill.Slug,
            ["routing_tag_ids"] = skill.RoutingTags.Select(t => t.TagId).Distinct().OrderBy(i => i).ToList(),
            ["routing_department_ids"] = skill.RoutingDepartments.Select(d => d.DepartmentId).Distinct().OrderBy(i => i).ToList(),
            ["agents"] = skill.AgentSkills.OrderBy(a => a.Id).Select(a => new Dictionary<string, object?>
            {
                ["user_id"] = a.UserId,
                ["proficiency"] = a.Proficiency,
            }).ToList(),
        }, ct));
    }

    [HttpPost("skills")]
    [EscalatedPage(Negotiate = true)]
    public Task<IActionResult> StoreSkill(CancellationToken ct) => SaveSkillAsync(null, ct);

    [HttpPut("skills/{id:int}")]
    [EscalatedPage(Negotiate = true)]
    public Task<IActionResult> UpdateSkill(int id, CancellationToken ct) => SaveSkillAsync(id, ct);

    /// <summary>The ids in an integer-array field, each checked against <paramref name="known"/>.</summary>
    private static List<int> KnownIds(UiInput input, string key, HashSet<int> known, Dictionary<string, string> errors)
    {
        var ids = new List<int>();
        if (input.Json(key) is not { } value || value.ValueKind == JsonValueKind.Null) return ids;
        if (value.ValueKind != JsonValueKind.Array)
        {
            errors[key] = $"The {key} must be a list.";
            return ids;
        }

        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            var ok = item.ValueKind == JsonValueKind.Number ? item.TryGetInt32(out var id)
                : int.TryParse(item.ValueKind == JsonValueKind.String ? item.GetString() : null, out id);
            if (!ok || !known.Contains(id)) errors[$"{key}.{index}"] = $"The selected {key}.{index} is invalid.";
            else if (!ids.Contains(id)) ids.Add(id);
            index++;
        }

        return ids;
    }

    private async Task<IActionResult> SaveSkillAsync(int? id, CancellationToken ct)
    {
        var skill = id is null ? new Skill { CreatedAt = DateTime.UtcNow } : await _db.Skills.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (skill is null) return NotFound();

        var input = await UiInput.ReadAsync(Request, ct);
        var errors = Errors();
        var name = RequiredName(input, 100, errors);
        if (!errors.ContainsKey("name"))
        {
            var lowered = name!.ToLowerInvariant();
            if (await _db.Skills.AnyAsync(s => s.Name.ToLower() == lowered && s.Id != (id ?? 0), ct))
            {
                errors["name"] = "The name has already been taken.";
            }
        }

        var tagIds = KnownIds(input, "routing_tag_ids", (await _db.Tags.Select(t => t.Id).ToListAsync(ct)).ToHashSet(), errors);
        var departmentIds = KnownIds(input, "routing_department_ids", (await _db.Departments.Select(d => d.Id).ToListAsync(ct)).ToHashSet(), errors);

        // Agents: staff only (the agent or admin role), each once, proficiency 1-5.
        var staff = (await _present.AgentOptionsAsync(ct)).Select(a => a["id"] as string).ToHashSet();
        var agents = new List<(string UserId, int Proficiency)>();
        if (input.Json("agents") is { ValueKind: JsonValueKind.Array } rawAgents)
        {
            var index = 0;
            foreach (var item in rawAgents.EnumerateArray())
            {
                var userId = item.ValueKind == JsonValueKind.Object && item.TryGetProperty("user_id", out var u)
                    ? u.ValueKind switch
                    {
                        JsonValueKind.String => u.GetString(),
                        JsonValueKind.Number => u.GetRawText(),
                        _ => null,
                    }
                    : null;
                if (string.IsNullOrEmpty(userId)) errors[$"agents.{index}.user_id"] = "The agent field is required.";
                else if (!staff.Contains(userId)) errors[$"agents.{index}.user_id"] = $"The selected agents.{index}.user_id is not an agent.";
                else if (agents.Any(a => a.UserId == userId)) errors[$"agents.{index}.user_id"] = $"The agents.{index}.user_id field has a duplicate value.";

                var proficiency = item.ValueKind == JsonValueKind.Object && item.TryGetProperty("proficiency", out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var level) ? level : 0;
                if (proficiency is < 1 or > 5) errors[$"agents.{index}.proficiency"] = "The proficiency must be between 1 and 5.";

                agents.Add((userId ?? string.Empty, proficiency));
                index++;
            }
        }
        else if (input.Json("agents") is { ValueKind: not JsonValueKind.Null })
        {
            errors["agents"] = "The agents must be a list.";
        }

        var formUrl = id is null ? $"{Prefix}/admin/skills/create" : $"{Prefix}/admin/skills/{id}/edit";
        if (errors.Count > 0) return UiResults.Back(HttpContext, formUrl, errors: errors);

        var now = DateTime.UtcNow;
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        skill.Name = name!;
        skill.UpdatedAt = now;
        if (id is null)
        {
            var baseSlug = Skill.GenerateSlug(skill.Name);
            if (string.IsNullOrEmpty(baseSlug)) baseSlug = "skill";
            var slug = baseSlug;
            for (var n = 2; await _db.Skills.AnyAsync(s => s.Slug == slug, ct); n++) slug = $"{baseSlug}-{n}";
            skill.Slug = slug;
            _db.Skills.Add(skill);
            await _db.SaveChangesAsync(ct);
        }

        await _db.SkillRoutingTags.Where(r => r.SkillId == skill.Id).ExecuteDeleteAsync(ct);
        await _db.SkillRoutingDepartments.Where(r => r.SkillId == skill.Id).ExecuteDeleteAsync(ct);
        await _db.AgentSkills.Where(a => a.SkillId == skill.Id).ExecuteDeleteAsync(ct);
        foreach (var tagId in tagIds) _db.SkillRoutingTags.Add(new SkillRoutingTag { SkillId = skill.Id, TagId = tagId });
        foreach (var departmentId in departmentIds) _db.SkillRoutingDepartments.Add(new SkillRoutingDepartment { SkillId = skill.Id, DepartmentId = departmentId });
        foreach (var (userId, proficiency) in agents)
        {
            _db.AgentSkills.Add(new AgentSkill { SkillId = skill.Id, UserId = userId, Proficiency = proficiency, CreatedAt = now, UpdatedAt = now });
        }

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new UiRedirectResult($"{Prefix}/admin/skills") { Success = id is null ? "Skill created." : "Skill updated." };
    }

    [HttpDelete("skills/{id:int}")]
    [EscalatedPage(Negotiate = true)]
    public async Task<IActionResult> DestroySkill(int id, CancellationToken ct)
    {
        var skill = await _db.Skills.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (skill is null) return NotFound();

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await _db.SkillRoutingTags.Where(r => r.SkillId == id).ExecuteDeleteAsync(ct);
        await _db.SkillRoutingDepartments.Where(r => r.SkillId == id).ExecuteDeleteAsync(ct);
        await _db.AgentSkills.Where(a => a.SkillId == id).ExecuteDeleteAsync(ct);
        _db.Skills.Remove(skill);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new UiRedirectResult($"{Prefix}/admin/skills") { Success = "Skill deleted." };
    }

    // --- Capacity ---

    /// <summary>Laravel's <c>AgentCapacity::loadPercentage()</c>.</summary>
    private static double LoadPercentage(AgentCapacity capacity) =>
        capacity.MaxConcurrent <= 0 ? 100.0 : Math.Round(capacity.CurrentCount * 100.0 / capacity.MaxConcurrent, 1, MidpointRounding.AwayFromZero);

    [HttpGet("capacity")]
    [EscalatedPage(Negotiate = true)]
    public async Task<IActionResult> Capacity(CancellationToken ct)
    {
        var rows = await _db.AgentCapacities.AsNoTracking().OrderBy(c => c.UserId).ThenBy(c => c.Id).ToListAsync(ct);
        var capacities = new List<Dictionary<string, object?>>();
        foreach (var row in rows)
        {
            var user = await _present.UserAsync(row.UserId, ct);
            capacities.Add(new Dictionary<string, object?>
            {
                ["id"] = row.Id,
                ["user_id"] = row.UserId,
                ["agent_name"] = user?["name"] ?? "Unknown",
                ["channel"] = row.Channel,
                ["max_concurrent"] = row.MaxConcurrent,
                ["current_count"] = row.CurrentCount,
                ["load_percentage"] = LoadPercentage(row),
            });
        }

        return UiResults.Render("Escalated/Admin/Capacity/Index", new { capacities });
    }

    [HttpPut("capacity/{id:int}")]
    public async Task<IActionResult> UpdateCapacity(int id, CancellationToken ct)
    {
        var capacity = await _db.AgentCapacities.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (capacity is null) return NotFound();

        var input = await UiInput.ReadAsync(Request, ct);
        var back = $"{Prefix}/admin/capacity";
        var max = input.Int("max_concurrent") ?? 0;
        if (max is < 1 or > 999)
        {
            return UiResults.Back(HttpContext, back, errors: new Dictionary<string, string>
            {
                ["max_concurrent"] = "The max concurrent must be between 1 and 999.",
            });
        }

        capacity.MaxConcurrent = max;
        capacity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return UiResults.Back(HttpContext, back, success: "Capacity updated.");
    }

    // --- Audit log ---

    /// <summary>
    /// The verb of a stored action: this backend records some as <c>workflow.created</c>;
    /// the screen colours and filters by escalated-laravel's <c>created</c>.
    /// </summary>
    private static string AuditVerb(string action) => action.Contains('.') ? action[(action.LastIndexOf('.') + 1)..] : action;

    [HttpGet("audit-log")]
    public async Task<IActionResult> AuditLog(CancellationToken ct)
    {
        var filters = new Dictionary<string, string?>();
        foreach (var key in new[] { "user_id", "action", "auditable_type", "date_from", "date_to" })
        {
            var value = Request.Query[key].ToString();
            if (!string.IsNullOrEmpty(value)) filters[key] = value;
        }

        var query = _db.AuditLogs.AsNoTracking().AsQueryable();
        if (filters.GetValueOrDefault("user_id") is { } userId) query = query.Where(l => l.UserId == userId);
        if (filters.GetValueOrDefault("action") is { } action)
        {
            var dotted = "." + action;
            query = query.Where(l => l.Action == action || l.Action.EndsWith(dotted));
        }

        if (filters.GetValueOrDefault("auditable_type") is { } type) query = query.Where(l => l.EntityType == type);
        if (filters.GetValueOrDefault("date_from") is { } from
            && DateTime.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var since))
        {
            query = query.Where(l => l.CreatedAt >= since);
        }

        if (filters.GetValueOrDefault("date_to") is { } to
            && DateTime.TryParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var until))
        {
            var end = until.AddDays(1);
            query = query.Where(l => l.CreatedAt < end);
        }

        const int perPage = 50;
        var page = int.TryParse(Request.Query["page"], out var requested) && requested > 0 ? requested : 1;
        var total = await query.CountAsync(ct);
        var logs = await query.OrderByDescending(l => l.CreatedAt).ThenByDescending(l => l.Id)
            .Skip((page - 1) * perPage).Take(perPage).ToListAsync(ct);

        var rows = new List<object?>();
        foreach (var log in logs)
        {
            var user = await _present.UserAsync(log.UserId, ct);
            rows.Add(new Dictionary<string, object?>
            {
                ["id"] = log.Id,
                ["user_id"] = log.UserId,
                ["user"] = user is null ? null : new Dictionary<string, object?> { ["id"] = user["id"], ["name"] = user["name"] },
                ["action"] = AuditVerb(log.Action),
                ["auditable_type"] = log.EntityType,
                ["auditable_id"] = log.EntityId,
                ["old_values"] = UiPresenter.ParseJson(log.OldValues),
                ["new_values"] = UiPresenter.ParseJson(log.NewValues),
                ["ip_address"] = log.IpAddress,
                ["user_agent"] = log.UserAgent,
                ["created_at"] = log.CreatedAt,
            });
        }

        var userIds = await _db.AuditLogs.AsNoTracking().Where(l => l.UserId != null).Select(l => l.UserId!).Distinct().ToListAsync(ct);
        var users = new List<Dictionary<string, object?>>();
        foreach (var id in userIds)
        {
            var user = await _present.UserAsync(id, ct);
            users.Add(new Dictionary<string, object?> { ["id"] = id, ["name"] = user?["name"] });
        }

        var types = await _db.AuditLogs.AsNoTracking().Select(l => l.EntityType).Distinct().ToListAsync(ct);

        return UiResults.Render("Escalated/Admin/AuditLog/Index", new
        {
            logs = UiPresenter.Paginator(rows, total, page, perPage, $"{Prefix}/admin/audit-log", filters),
            filters,
            users = users.OrderBy(u => u["name"] as string, StringComparer.OrdinalIgnoreCase).ToList(),
            actions = AuditActions,
            resourceTypes = types.OrderBy(t => t, StringComparer.Ordinal).ToList(),
        });
    }
}
