using System.Text.Json;
using Escalated.Configuration;
using Escalated.Data;
using Escalated.Models.Newsletter;
using Escalated.Services.Newsletter;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Escalated.Controllers.Newsletter;

[ApiController]
[NewsletterEnabled]
public class AdminNewsletterTemplateController : ControllerBase
{
    private readonly EscalatedDbContext _db;
    private readonly NewsletterPermissionService _permissions;
    private readonly IOptions<EscalatedOptions> _options;

    public AdminNewsletterTemplateController(
        EscalatedDbContext db,
        NewsletterPermissionService permissions,
        IOptions<EscalatedOptions> options)
    {
        _db = db;
        _permissions = permissions;
        _options = options;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        await _permissions.RequireAsync(HttpContext, "newsletters.manage", ct);
        var templates = await _db.NewsletterTemplates
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);
        return NewsletterHttp.Inertia(this, "Escalated/Admin/Newsletters/Templates/Index", new { templates });
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        await _permissions.RequireAsync(HttpContext, "newsletters.manage", ct);
        return NewsletterHttp.Inertia(this, "Escalated/Admin/Newsletters/Templates/Create", new { themes = DiscoverThemes() });
    }

    [HttpPost]
    public async Task<IActionResult> Store([FromBody] JsonElement body, CancellationToken ct)
    {
        await _permissions.RequireAsync(HttpContext, "newsletters.manage", ct);
        var template = ValidateForm(body);
        template.CreatedBy = UserId();
        _db.NewsletterTemplates.Add(template);
        await _db.SaveChangesAsync(ct);
        return NewsletterHttp.Redirect(this, "/admin/newsletters/templates");
    }

    [HttpGet]
    public async Task<IActionResult> Show(int template, CancellationToken ct)
    {
        await _permissions.RequireAsync(HttpContext, "newsletters.manage", ct);
        return NewsletterHttp.Inertia(this, "Escalated/Admin/Newsletters/Templates/Show", new
        {
            template = await FindTemplateAsync(template, ct),
            themes = DiscoverThemes(),
            isNew = false,
        });
    }

    [HttpPut]
    public async Task<IActionResult> Update(int template, [FromBody] JsonElement body, CancellationToken ct)
    {
        await _permissions.RequireAsync(HttpContext, "newsletters.manage", ct);
        var entity = await FindTemplateAsync(template, ct);
        var data = ValidateForm(body);
        entity.Name = data.Name;
        entity.Theme = data.Theme;
        entity.SubjectTemplate = data.SubjectTemplate;
        entity.BodyMarkdown = data.BodyMarkdown;
        entity.MergeFieldsSchema = data.MergeFieldsSchema;
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return NewsletterHttp.Redirect(this, $"/admin/newsletters/templates/{entity.Id}");
    }

    [HttpDelete]
    public async Task<IActionResult> Destroy(int template, CancellationToken ct)
    {
        await _permissions.RequireAsync(HttpContext, "newsletters.manage", ct);
        var entity = await FindTemplateAsync(template, ct);
        _db.NewsletterTemplates.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return NewsletterHttp.Redirect(this, "/admin/newsletters/templates");
    }

    private NewsletterTemplate ValidateForm(JsonElement body) => new()
    {
        Name = NewsletterHttp.RequiredString(body, "name", 255),
        Theme = NewsletterHttp.RequiredString(body, "theme", 64),
        SubjectTemplate = NewsletterHttp.OptionalString(body, "subject_template", 998),
        BodyMarkdown = NewsletterHttp.RequiredString(body, "body_markdown"),
        MergeFieldsSchema = NewsletterHttp.JsonObjectOrArray(body, "merge_fields_schema"),
    };

    private async Task<NewsletterTemplate> FindTemplateAsync(int id, CancellationToken ct) =>
        await _db.NewsletterTemplates.SingleOrDefaultAsync(t => t.Id == id, ct)
        ?? throw new InvalidOperationException($"Newsletter template #{id} not found");

    private string[] DiscoverThemes()
    {
        var dir = _options.Value.Newsletters.ThemesDir;
        if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
            return Directory.GetFiles(dir, "*.html").Select(f => Path.GetFileNameWithoutExtension(f)).ToArray();
        return ["default", "branded"];
    }

    private string? UserId() => User.FindFirst("sub")?.Value ?? User.FindFirst("id")?.Value ?? User.Identity?.Name;
}
