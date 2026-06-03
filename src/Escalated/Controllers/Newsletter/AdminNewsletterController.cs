using System.Security.Cryptography;
using System.Text.Json;
using Escalated.Configuration;
using Escalated.Data;
using Escalated.Models;
using Escalated.Services.Newsletter;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NewsletterDelivery = Escalated.Models.Newsletter.NewsletterDelivery;
using NewsletterEntity = Escalated.Models.Newsletter.Newsletter;

namespace Escalated.Controllers.Newsletter;

[ApiController]
public class AdminNewsletterController : ControllerBase
{
    private readonly EscalatedDbContext _db;
    private readonly NewsletterPermissionService _permissions;
    private readonly NewsletterPlanner _planner;
    private readonly NewsletterRenderer _renderer;
    private readonly INewsletterEmailSender _sender;
    private readonly IOptions<EscalatedOptions> _options;

    public AdminNewsletterController(
        EscalatedDbContext db,
        NewsletterPermissionService permissions,
        NewsletterPlanner planner,
        NewsletterRenderer renderer,
        INewsletterEmailSender sender,
        IOptions<EscalatedOptions> options)
    {
        _db = db;
        _permissions = permissions;
        _planner = planner;
        _renderer = renderer;
        _sender = sender;
        _options = options;
    }

    public async Task<IActionResult> Index([FromQuery] string tab = "drafts", CancellationToken ct = default)
    {
        await _permissions.RequireAsync(HttpContext, "newsletters.manage", ct);
        var statuses = tab switch
        {
            "scheduled" => new[] { "scheduled", "sending", "paused" },
            "sent" => new[] { "sent", "failed" },
            _ => new[] { "draft" },
        };
        var newsletters = await _db.Newsletters
            .Include(n => n.TargetList)
            .Where(n => statuses.Contains(n.Status))
            .OrderByDescending(n => n.CreatedAt)
            .Take(50)
            .ToListAsync(ct);
        return NewsletterHttp.Inertia(this, "Escalated/Admin/Newsletters/Index", new { newsletters, tab });
    }

    public async Task<IActionResult> Create(CancellationToken ct = default)
    {
        await _permissions.RequireAsync(HttpContext, "newsletters.manage", ct);
        return NewsletterHttp.Inertia(this, "Escalated/Admin/Newsletters/Compose", await ComposePropsAsync(ct));
    }

    public async Task<IActionResult> Store([FromBody] JsonElement body, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(HttpContext, "newsletters.manage", ct);
        var data = await ValidateFormAsync(body, ct);
        if (data.Status is "scheduled" or "sending")
        {
            await _permissions.RequireAsync(HttpContext, "newsletters.send", ct);
            if (!MailConfigured()) return BadRequest(new { from_email = "Outbound mail is not configured." });
        }

        data.CreatedBy = UserId();
        _db.Newsletters.Add(data);
        await _db.SaveChangesAsync(ct);
        if (data.Status == "sending")
            await _planner.PlanAsync(data, ct);
        return NewsletterHttp.Redirect(this, $"/admin/newsletters/{data.Id}");
    }

    public async Task<IActionResult> Preview([FromBody] JsonElement body, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(HttpContext, "newsletters.manage", ct);
        var newsletter = new NewsletterEntity
        {
            Id = 0,
            Subject = NewsletterHttp.OptionalString(body, "subject", 998) ?? "",
            FromEmail = NewsletterHttp.Email(NewsletterHttp.OptionalString(body, "from_email", 320), "from_email")
                ?? "preview@example.test",
            Theme = NewsletterHttp.OptionalString(body, "theme", 64) ?? "default",
            BodyMarkdown = NewsletterHttp.OptionalString(body, "body_markdown"),
            TargetListId = NewsletterHttp.OptionalInt(body, "target_list_id") ?? 0,
        };
        var contact = new Contact { Id = 0, Email = "preview@example.test", Name = "Preview User" };
        var delivery = PreviewDelivery(newsletter, contact, "preview");
        return Ok(new { html = _renderer.Render(delivery, newsletter, contact) });
    }

    public async Task<IActionResult> Test([FromBody] JsonElement body, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(HttpContext, "newsletters.send", ct);
        var newsletter = await ValidateFormAsync(body, ct);
        var email = User.FindFirst("email")?.Value ?? newsletter.FromEmail;
        var contact = new Contact { Id = 0, Email = email, Name = User.Identity?.Name ?? "Tester" };
        var delivery = PreviewDelivery(newsletter, contact, GenerateToken());
        delivery.IsTest = true;
        var html = _renderer.Render(delivery, newsletter, contact);
        await _sender.SendAsync(delivery, html, ct);
        return Ok(new { ok = true });
    }

    public async Task<IActionResult> Show(int newsletter, [FromQuery] string tab = "overview", [FromQuery] string? status = null, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(HttpContext, "newsletters.manage", ct);
        var entity = await FindNewsletterAsync(newsletter, ct);
        var query = _db.NewsletterDeliveries
            .Include(d => d.Contact)
            .Where(d => d.NewsletterId == entity.Id && !d.IsTest);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(d => d.Status == status);
        var deliveries = await query.OrderByDescending(d => d.Id).Take(100).ToListAsync(ct);
        return NewsletterHttp.Inertia(this, "Escalated/Admin/Newsletters/Show", new
        {
            newsletter = entity,
            deliveries,
            topClicks = Array.Empty<object>(),
            tab,
        });
    }

    public async Task<IActionResult> Edit(int newsletter, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(HttpContext, "newsletters.manage", ct);
        var entity = await FindNewsletterAsync(newsletter, ct);
        if (entity.Status is not ("draft" or "scheduled"))
            return UnprocessableEntity("Only drafts and scheduled newsletters can be edited");
        return NewsletterHttp.Inertia(this, "Escalated/Admin/Newsletters/Edit", new
        {
            compose = await ComposePropsAsync(ct),
            newsletter = entity,
        });
    }

    public async Task<IActionResult> Update(int newsletter, [FromBody] JsonElement body, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(HttpContext, "newsletters.manage", ct);
        var entity = await FindNewsletterAsync(newsletter, ct);
        var data = await ValidateFormAsync(body, ct);
        if (data.Status is "scheduled" or "sending")
            await _permissions.RequireAsync(HttpContext, "newsletters.send", ct);

        entity.Subject = data.Subject;
        entity.FromEmail = data.FromEmail;
        entity.FromName = data.FromName;
        entity.ReplyTo = data.ReplyTo;
        entity.TargetListId = data.TargetListId;
        entity.TemplateId = data.TemplateId;
        entity.Theme = data.Theme;
        entity.BodyMarkdown = data.BodyMarkdown;
        entity.Status = data.Status;
        entity.ScheduledAt = data.ScheduledAt;
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        if (entity.Status == "sending")
            await _planner.PlanAsync(entity, ct);
        return NewsletterHttp.Redirect(this, $"/admin/newsletters/{entity.Id}");
    }

    public async Task<IActionResult> Destroy(int newsletter, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(HttpContext, "newsletters.manage", ct);
        var entity = await FindNewsletterAsync(newsletter, ct);
        if (entity.Status != "draft") return UnprocessableEntity("Only drafts can be deleted");
        _db.Newsletters.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return NewsletterHttp.Redirect(this, "/admin/newsletters");
    }

    private async Task<object> ComposePropsAsync(CancellationToken ct)
    {
        var lists = await _db.NewsletterLists
            .Select(l => new
            {
                l.Id,
                l.Name,
                member_count = _db.NewsletterListMembers.Count(m => m.ListId == l.Id),
            })
            .ToListAsync(ct);

        return new
        {
            lists,
            templates = await _db.NewsletterTemplates.Select(t => new { t.Id, t.Name }).ToListAsync(ct),
            themes = DiscoverThemes(),
            mailConfigured = MailConfigured(),
            canSend = true,
            defaultFromEmail = _options.Value.Newsletters.DefaultFrom,
            defaultReplyTo = _options.Value.Newsletters.DefaultReplyTo,
            defaultTheme = _options.Value.Newsletters.DefaultTheme,
        };
    }

    private async Task<NewsletterEntity> ValidateFormAsync(JsonElement body, CancellationToken ct)
    {
        var targetListId = NewsletterHttp.RequiredInt(body, "target_list_id");
        if (!await _db.NewsletterLists.AnyAsync(l => l.Id == targetListId, ct))
            throw new InvalidOperationException("target_list_id does not exist");

        var templateId = NewsletterHttp.OptionalInt(body, "template_id");
        if (templateId is not null && !await _db.NewsletterTemplates.AnyAsync(t => t.Id == templateId, ct))
            throw new InvalidOperationException("template_id does not exist");

        return new NewsletterEntity
        {
            Subject = NewsletterHttp.RequiredString(body, "subject", 998),
            FromEmail = NewsletterHttp.Email(NewsletterHttp.RequiredString(body, "from_email", 320), "from_email", true)!,
            FromName = NewsletterHttp.OptionalString(body, "from_name", 255),
            ReplyTo = NewsletterHttp.Email(NewsletterHttp.OptionalString(body, "reply_to", 320), "reply_to"),
            TargetListId = targetListId,
            TemplateId = templateId,
            Theme = NewsletterHttp.OptionalString(body, "theme", 64),
            BodyMarkdown = NewsletterHttp.OptionalString(body, "body_markdown"),
            Status = NewsletterHttp.OneOf(NewsletterHttp.OptionalString(body, "status") ?? "draft", "status", "draft", "scheduled", "sending"),
            ScheduledAt = NewsletterHttp.OptionalFutureDate(body, "scheduled_at"),
        };
    }

    private async Task<NewsletterEntity> FindNewsletterAsync(int id, CancellationToken ct) =>
        await _db.Newsletters
            .Include(n => n.TargetList)
            .Include(n => n.Template)
            .SingleOrDefaultAsync(n => n.Id == id, ct)
        ?? throw new InvalidOperationException($"Newsletter #{id} not found");

    private bool MailConfigured() => _options.Value.Newsletters.DefaultFrom is not null;

    private string? UserId() => User.FindFirst("sub")?.Value ?? User.FindFirst("id")?.Value ?? User.Identity?.Name;

    private static NewsletterDelivery PreviewDelivery(NewsletterEntity newsletter, Contact contact, string token) => new()
    {
        NewsletterId = newsletter.Id,
        ContactId = contact.Id,
        EmailAtSend = contact.Email,
        Status = "pending",
        TrackingToken = token,
        Newsletter = newsletter,
        Contact = contact,
    };

    private static string GenerateToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(20)).ToLowerInvariant();

    private string[] DiscoverThemes()
    {
        var dir = _options.Value.Newsletters.ThemesDir;
        if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
            return Directory.GetFiles(dir, "*.html").Select(f => Path.GetFileNameWithoutExtension(f)).ToArray();
        return ["default", "branded"];
    }
}
