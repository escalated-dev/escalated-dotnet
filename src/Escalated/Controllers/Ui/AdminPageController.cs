using System.Text.RegularExpressions;
using Escalated.Authorization;
using Escalated.Configuration;
using Escalated.Controllers.Widget;
using Escalated.Data;
using Escalated.Models;
using Escalated.Services;
using Escalated.Ui;
using Escalated.Widget;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Escalated.Controllers.Ui;

/// <summary>
/// The admin pages <c>Escalated/Admin/Settings</c>, <c>Departments/Index</c>,
/// <c>Departments/Form</c> and <c>Tags/Index</c>, with escalated-laravel's routes
/// and props (<c>Admin/SettingsController</c>, <c>DepartmentController</c>,
/// <c>TagController</c>). Several share a path with the JSON admin API, so those
/// answer only page requests (see <see cref="EscalatedPageAttribute.Negotiate"/>).
/// </summary>
[Route("support/admin")]
[Authorize(Policy = EscalatedPolicies.Admin)]
[EscalatedPage]
public partial class AdminPageController : ControllerBase
{
    /// <summary>
    /// The settings this backend reads from the settings table, and so the ones the
    /// Settings page may save (escalated-laravel sends exactly the keys its update
    /// saves as <c>supported_settings</c>, and the page hides the rest). Attachment
    /// limits, customer close and the reference prefix are configuration here
    /// (<c>Escalated:*</c>), not settings, so they are not offered.
    /// </summary>
    public static readonly IReadOnlyList<string> SupportedSettings = new[]
    {
        GuestSettings.GuestTicketsEnabled,
        "show_powered_by",
        "knowledge_base_public",
        GuestSettings.WidgetEnabled,
        "widget_color",
        "widget_position",
        "widget_greeting",
        WidgetOriginPolicy.SettingKey,
        // The two-factor page's "require for all staff" switch posts only this key.
        "two_factor_required",
    };

    private static readonly HashSet<string> BooleanSettings = new()
    {
        GuestSettings.GuestTicketsEnabled, "show_powered_by", "knowledge_base_public", GuestSettings.WidgetEnabled,
        "two_factor_required",
    };

    private readonly EscalatedDbContext _db;
    private readonly SettingsService _settings;
    private readonly UiPresenter _present;
    private readonly EscalatedOptions _options;

    public AdminPageController(EscalatedDbContext db, SettingsService settings,
        UiPresenter present, IOptions<EscalatedOptions> options)
    {
        _db = db;
        _settings = settings;
        _present = present;
        _options = options.Value;
    }

    private string Prefix => "/" + _options.RoutePrefix.Trim('/');

    // --- Settings ---

    [HttpGet("settings")]
    [EscalatedPage(Negotiate = true)]
    public async Task<IActionResult> Settings(CancellationToken ct)
    {
        async Task<bool> On(string key) => await GuestSettings.IsOnAsync(_settings, key, ct);

        return UiResults.Render("Escalated/Admin/Settings", new
        {
            settings = new Dictionary<string, object?>
            {
                [GuestSettings.GuestTicketsEnabled] = await On(GuestSettings.GuestTicketsEnabled),
                ["show_powered_by"] = await On("show_powered_by"),
                ["knowledge_base_public"] = await On("knowledge_base_public"),
                [GuestSettings.WidgetEnabled] = await On(GuestSettings.WidgetEnabled),
                ["widget_color"] = await _settings.GetAsync("widget_color", "#4F46E5", ct),
                ["widget_position"] = await _settings.GetAsync("widget_position", "bottom-right", ct),
                ["widget_greeting"] = await _settings.GetAsync("widget_greeting", "Hi there! How can we help?", ct),
                [WidgetOriginPolicy.SettingKey] = await _settings.GetAsync(WidgetOriginPolicy.SettingKey, string.Empty, ct),
                ["two_factor_required"] = Authorization.AccessFeatures.Truthy(await _settings.GetAsync("two_factor_required", "0", ct)),
            },
            supported_settings = SupportedSettings,
            departments = await _present.DepartmentOptionsAsync(ct),
            update_url = $"{Prefix}/admin/settings",
        });
    }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColor();

    [HttpPost("settings")]
    [EscalatedPage(Negotiate = true)]
    public async Task<IActionResult> SaveSettings(CancellationToken ct)
    {
        var input = await UiInput.ReadAsync(Request, ct);
        var errors = new Dictionary<string, string>();
        var values = new Dictionary<string, string?>();

        foreach (var key in SupportedSettings.Where(input.Has))
        {
            var raw = input.String(key);
            if (BooleanSettings.Contains(key))
            {
                if (raw?.ToLowerInvariant() is not ("1" or "0" or "true" or "false"))
                {
                    errors[key] = $"The {key.Replace('_', ' ')} field must be true or false.";
                    continue;
                }

                values[key] = raw is "1" or "true" ? "1" : "0";
                continue;
            }

            switch (key)
            {
                case "widget_color" when raw is null || !HexColor().IsMatch(raw):
                    errors[key] = "The widget color must be a hex color like #4F46E5.";
                    break;
                case "widget_position" when raw is not ("bottom-right" or "bottom-left"):
                    errors[key] = "The selected widget position is invalid.";
                    break;
                case "widget_greeting" when raw is { Length: > 255 }:
                    errors[key] = "The widget greeting may not be greater than 255 characters.";
                    break;
                case var _ when key == WidgetOriginPolicy.SettingKey:
                    var entries = WidgetOrigins.Split(raw).ToList();
                    if (entries.FirstOrDefault(e => WidgetOrigins.NormalizePattern(e) is null) is { } bad)
                    {
                        errors[key] = $"\"{bad}\" is not a valid origin. Use scheme://host[:port] or https://*.example.com.";
                        break;
                    }

                    values[key] = string.Join('\n', WidgetOrigins.Patterns(entries));
                    break;
                default:
                    values[key] = raw;
                    break;
            }
        }

        var back = $"{Prefix}/admin/settings";
        if (errors.Count > 0) return UiResults.Back(HttpContext, back, errors: errors);

        foreach (var (key, value) in values)
        {
            await _settings.SetAsync(key, value, ct);
        }

        return UiResults.Back(HttpContext, back, success: "Settings updated.");
    }

    // --- Departments ---

    [HttpGet("departments")]
    [EscalatedPage(Negotiate = true)]
    public async Task<IActionResult> Departments(CancellationToken ct)
    {
        var departments = await _db.Departments.AsNoTracking().OrderBy(d => d.Name)
            .Select(d => new
            {
                Department = d,
                Tickets = _db.Tickets.IgnoreQueryFilters().Count(t => t.DepartmentId == d.Id && t.DeletedAt == null),
                Agents = _db.DepartmentAgents.Count(a => a.DepartmentId == d.Id),
            })
            .ToListAsync(ct);

        return UiResults.Render("Escalated/Admin/Departments/Index", new
        {
            departments = departments.Select(row =>
            {
                var item = UiPresenter.Department(row.Department);
                item["tickets_count"] = row.Tickets;
                item["agents_count"] = row.Agents;
                item["created_at"] = row.Department.CreatedAt;
                return item;
            }).ToList(),
        });
    }

    [HttpGet("departments/create")]
    public IActionResult CreateDepartment() =>
        UiResults.Render("Escalated/Admin/Departments/Form", new Dictionary<string, object?> { ["department"] = null });

    [HttpGet("departments/{id:int}/edit")]
    public async Task<IActionResult> EditDepartment(int id, CancellationToken ct)
    {
        var department = await _db.Departments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);
        if (department is null) return NotFound();

        var item = UiPresenter.Department(department);
        var agents = new List<Dictionary<string, object?>>();
        foreach (var agentId in await _db.DepartmentAgents.Where(a => a.DepartmentId == id).Select(a => a.AgentId).ToListAsync(ct))
        {
            agents.Add((await _present.UserAsync(agentId, ct))!);
        }
        item["agents"] = agents;

        return UiResults.Render("Escalated/Admin/Departments/Form", new { department = item });
    }

    [HttpPost("departments")]
    [EscalatedPage(Negotiate = true)]
    public Task<IActionResult> StoreDepartment(CancellationToken ct) => SaveDepartmentAsync(null, ct);

    [HttpPut("departments/{id:int}")]
    [EscalatedPage(Negotiate = true)]
    public Task<IActionResult> UpdateDepartment(int id, CancellationToken ct) => SaveDepartmentAsync(id, ct);

    private async Task<IActionResult> SaveDepartmentAsync(int? id, CancellationToken ct)
    {
        var department = id is null ? new Department { CreatedAt = DateTime.UtcNow }
            : await _db.Departments.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (department is null) return NotFound();

        var input = await UiInput.ReadAsync(Request, ct);
        var name = input.String("name");
        var slug = input.String("slug");
        var errors = new Dictionary<string, string>();
        if (name is null) errors["name"] = "The name field is required.";
        else if (name.Length > 255) errors["name"] = "The name may not be greater than 255 characters.";
        if (slug is { Length: > 255 }) errors["slug"] = "The slug may not be greater than 255 characters.";

        var formUrl = id is null ? $"{Prefix}/admin/departments/create" : $"{Prefix}/admin/departments/{id}/edit";
        if (errors.Count > 0) return UiResults.Back(HttpContext, formUrl, errors: errors);

        slug = Department.GenerateSlug(slug ?? name!);
        if (await _db.Departments.AnyAsync(d => d.Slug == slug && d.Id != department.Id, ct))
        {
            return UiResults.Back(HttpContext, formUrl, errors: new Dictionary<string, string> { ["slug"] = "The slug has already been taken." });
        }

        department.Name = name!;
        department.Slug = slug;
        department.Description = input.String("description");
        department.IsActive = !input.Has("is_active") || input.Bool("is_active");
        department.UpdatedAt = DateTime.UtcNow;
        if (id is null) _db.Departments.Add(department);
        await _db.SaveChangesAsync(ct);

        return new UiRedirectResult($"{Prefix}/admin/departments") { Success = id is null ? "Department created." : "Department updated." };
    }

    /// <summary>Deletes the department; its tickets keep going with no department.</summary>
    [HttpDelete("departments/{id:int}")]
    [EscalatedPage(Negotiate = true)]
    public async Task<IActionResult> DestroyDepartment(int id, CancellationToken ct)
    {
        var department = await _db.Departments.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (department is null) return NotFound();
        var index = $"{Prefix}/admin/departments";

        if (await _db.SlaPolicies.AnyAsync(p => p.DepartmentId == id, ct))
        {
            return new UiRedirectResult(index) { Error = "An SLA policy targets this department; change it first." };
        }

        await _db.Tickets.IgnoreQueryFilters().Where(t => t.DepartmentId == id)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.DepartmentId, (int?)null), ct);
        _db.Departments.Remove(department);
        await _db.SaveChangesAsync(ct);

        return new UiRedirectResult(index) { Success = "Department deleted." };
    }

    // --- Tags ---

    [HttpGet("tags")]
    [EscalatedPage(Negotiate = true)]
    public async Task<IActionResult> Tags(CancellationToken ct)
    {
        var tags = await _db.Tags.AsNoTracking().OrderBy(t => t.Name)
            .Select(t => new { Tag = t, Tickets = _db.TicketTags.Count(tt => tt.TagId == t.Id) })
            .ToListAsync(ct);

        return UiResults.Render("Escalated/Admin/Tags/Index", new
        {
            tags = tags.Select(row =>
            {
                var item = UiPresenter.Tag(row.Tag);
                item["tickets_count"] = row.Tickets;
                return item;
            }).ToList(),
        });
    }

    [HttpPost("tags")]
    [EscalatedPage(Negotiate = true)]
    public Task<IActionResult> StoreTag(CancellationToken ct) => SaveTagAsync(null, ct);

    [HttpPut("tags/{id:int}")]
    [EscalatedPage(Negotiate = true)]
    public Task<IActionResult> UpdateTag(int id, CancellationToken ct) => SaveTagAsync(id, ct);

    private async Task<IActionResult> SaveTagAsync(int? id, CancellationToken ct)
    {
        var tag = id is null ? new Tag { CreatedAt = DateTime.UtcNow } : await _db.Tags.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tag is null) return NotFound();

        var input = await UiInput.ReadAsync(Request, ct);
        var name = input.String("name");
        var slug = input.String("slug");
        var color = input.String("color");
        var errors = new Dictionary<string, string>();
        if (name is null) errors["name"] = "The name field is required.";
        else if (name.Length > 100) errors["name"] = "The name may not be greater than 100 characters.";
        if (slug is { Length: > 100 }) errors["slug"] = "The slug may not be greater than 100 characters.";
        if (color is { Length: > 7 }) errors["color"] = "The color may not be greater than 7 characters.";

        var back = $"{Prefix}/admin/tags";
        if (errors.Count > 0) return UiResults.Back(HttpContext, back, errors: errors);

        slug = Tag.GenerateSlug(slug ?? name!);
        if (await _db.Tags.AnyAsync(t => t.Slug == slug && t.Id != tag.Id, ct))
        {
            return UiResults.Back(HttpContext, back, errors: new Dictionary<string, string> { ["name"] = "A tag with this name already exists." });
        }

        tag.Name = name!;
        tag.Slug = slug;
        tag.Color = color;
        tag.UpdatedAt = DateTime.UtcNow;
        if (id is null) _db.Tags.Add(tag);
        await _db.SaveChangesAsync(ct);

        return UiResults.Back(HttpContext, back, success: id is null ? "Tag created." : "Tag updated.");
    }

    [HttpDelete("tags/{id:int}")]
    [EscalatedPage(Negotiate = true)]
    public async Task<IActionResult> DestroyTag(int id, CancellationToken ct)
    {
        var tag = await _db.Tags.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tag is null) return NotFound();

        _db.TicketTags.RemoveRange(await _db.TicketTags.IgnoreQueryFilters().Where(tt => tt.TagId == id).ToListAsync(ct));
        _db.Tags.Remove(tag);
        await _db.SaveChangesAsync(ct);

        return UiResults.Back(HttpContext, $"{Prefix}/admin/tags", success: "Tag deleted.");
    }
}
