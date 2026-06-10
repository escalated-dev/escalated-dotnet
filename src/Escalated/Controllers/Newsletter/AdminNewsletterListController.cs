using System.Text.Json;
using System.Text.RegularExpressions;
using Escalated.Data;
using Escalated.Models;
using Escalated.Models.Newsletter;
using Escalated.Services.Newsletter;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Controllers.Newsletter;

[ApiController]
[NewsletterEnabled]
public class AdminNewsletterListController : ControllerBase
{
    private static readonly Regex EmailRegex = new(
        @"^[^\s@]+@[^\s@]+\.[^\s@]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly EscalatedDbContext _db;
    private readonly NewsletterPermissionService _permissions;
    private readonly ContactSegmentResolver _segments;

    public AdminNewsletterListController(
        EscalatedDbContext db,
        NewsletterPermissionService permissions,
        ContactSegmentResolver segments)
    {
        _db = db;
        _permissions = permissions;
        _segments = segments;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        await _permissions.RequireAsync(HttpContext, "newsletters.manage", ct);
        var lists = await _db.NewsletterLists.ToListAsync(ct);
        var enriched = new List<object>();
        foreach (var list in lists)
            enriched.Add(await WithCountsAsync(list, ct));
        return NewsletterHttp.Inertia(this, "Escalated/Admin/Newsletters/Lists/Index", new { lists = enriched });
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        await _permissions.RequireAsync(HttpContext, "newsletters.manage", ct);
        return NewsletterHttp.Inertia(this, "Escalated/Admin/Newsletters/Lists/Create", new { });
    }

    [HttpPost]
    public async Task<IActionResult> Store([FromBody] JsonElement body, CancellationToken ct)
    {
        await _permissions.RequireAsync(HttpContext, "newsletters.manage", ct);
        var list = new NewsletterList
        {
            Name = NewsletterHttp.RequiredString(body, "name", 255),
            Description = NewsletterHttp.OptionalString(body, "description"),
            Kind = NewsletterHttp.OneOf(NewsletterHttp.OptionalString(body, "kind") ?? "static", "kind", "static", "dynamic"),
            FilterJson = NewsletterHttp.JsonObjectOrArray(body, "filter_json"),
            CreatedBy = UserId(),
        };
        _db.NewsletterLists.Add(list);
        await _db.SaveChangesAsync(ct);
        return NewsletterHttp.Redirect(this, $"/admin/newsletters/lists/{list.Id}");
    }

    [HttpGet]
    public async Task<IActionResult> Show(int list, CancellationToken ct)
    {
        await _permissions.RequireAsync(HttpContext, "newsletters.manage", ct);
        var entity = await FindListAsync(list, ct);
        var members = await _db.NewsletterListMembers
            .Include(m => m.Contact)
            .Where(m => m.ListId == entity.Id)
            .OrderByDescending(m => m.Id)
            .Take(100)
            .ToListAsync(ct);

        var matchCount = entity.Kind == "dynamic"
            ? await _segments.CountMatchesAsync(entity.FilterJson, ct)
            : 0;

        return NewsletterHttp.Inertia(this, "Escalated/Admin/Newsletters/Lists/Show", new
        {
            list = await WithCountsAsync(entity, ct),
            members,
            matchCount,
        });
    }

    [HttpPut]
    public async Task<IActionResult> Update(int list, [FromBody] JsonElement body, CancellationToken ct)
    {
        await _permissions.RequireAsync(HttpContext, "newsletters.manage", ct);
        var entity = await FindListAsync(list, ct);

        if (body.TryGetProperty("name", out _))
            entity.Name = NewsletterHttp.RequiredString(body, "name", 255);
        if (body.TryGetProperty("description", out _))
            entity.Description = NewsletterHttp.OptionalString(body, "description");
        if (body.TryGetProperty("filter_json", out _))
            entity.FilterJson = NewsletterHttp.JsonObjectOrArray(body, "filter_json");
        entity.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return NewsletterHttp.Redirect(this, $"/admin/newsletters/lists/{entity.Id}");
    }

    [HttpDelete]
    public async Task<IActionResult> Destroy(int list, CancellationToken ct)
    {
        await _permissions.RequireAsync(HttpContext, "newsletters.manage", ct);
        var entity = await FindListAsync(list, ct);
        _db.NewsletterLists.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return NewsletterHttp.Redirect(this, "/admin/newsletters/lists");
    }

    [HttpPost]
    public async Task<IActionResult> AddMember(int list, [FromBody] JsonElement body, CancellationToken ct)
    {
        await _permissions.RequireAsync(HttpContext, "newsletters.manage", ct);
        var entity = await FindListAsync(list, ct);
        AssertStatic(entity);

        var contactId = NewsletterHttp.RequiredInt(body, "contact_id");
        if (!await _db.Contacts.AnyAsync(c => c.Id == contactId, ct))
            throw new InvalidOperationException("contact_id does not exist");

        if (!await _db.NewsletterListMembers.AnyAsync(m => m.ListId == entity.Id && m.ContactId == contactId, ct))
        {
            _db.NewsletterListMembers.Add(new NewsletterListMember
            {
                ListId = entity.Id,
                ContactId = contactId,
                AddedBy = UserId(),
            });
            await _db.SaveChangesAsync(ct);
        }

        return NewsletterHttp.Redirect(this, $"/admin/newsletters/lists/{entity.Id}");
    }

    [HttpDelete]
    public async Task<IActionResult> RemoveMember(int list, int contactId, CancellationToken ct)
    {
        await _permissions.RequireAsync(HttpContext, "newsletters.manage", ct);
        var entity = await FindListAsync(list, ct);
        AssertStatic(entity);

        var member = await _db.NewsletterListMembers
            .SingleOrDefaultAsync(m => m.ListId == entity.Id && m.ContactId == contactId, ct);
        if (member is not null)
        {
            _db.NewsletterListMembers.Remove(member);
            await _db.SaveChangesAsync(ct);
        }

        return NewsletterHttp.Redirect(this, $"/admin/newsletters/lists/{entity.Id}");
    }

    [HttpPost]
    public async Task<IActionResult> ImportCsv(int list, IFormFile? file, CancellationToken ct)
    {
        await _permissions.RequireAsync(HttpContext, "newsletters.manage", ct);
        var entity = await FindListAsync(list, ct);
        AssertStatic(entity);

        if (file is null || file.Length == 0)
            throw new InvalidOperationException("file is required");

        using var reader = new StreamReader(file.OpenReadStream());
        var text = await reader.ReadToEndAsync(ct);
        var imported = 0;

        foreach (var line in text.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            var email = line.Split(',')[0]?.Trim();
            if (string.IsNullOrWhiteSpace(email) || !EmailRegex.IsMatch(email))
                continue;

            var name = line.Split(',', 2).ElementAtOrDefault(1)?.Trim();
            var normalized = Contact.NormalizeEmail(email);
            var contact = await _db.Contacts.SingleOrDefaultAsync(c => c.Email == normalized, ct);
            if (contact is null)
            {
                contact = new Contact
                {
                    Email = normalized,
                    Name = string.IsNullOrWhiteSpace(name) ? null : name,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                };
                _db.Contacts.Add(contact);
                await _db.SaveChangesAsync(ct);
            }

            if (!await _db.NewsletterListMembers.AnyAsync(m => m.ListId == entity.Id && m.ContactId == contact.Id, ct))
            {
                _db.NewsletterListMembers.Add(new NewsletterListMember
                {
                    ListId = entity.Id,
                    ContactId = contact.Id,
                    AddedBy = UserId(),
                });
                await _db.SaveChangesAsync(ct);
            }

            imported++;
        }

        return NewsletterHttp.Redirect(this, $"/admin/newsletters/lists/{entity.Id}", new { status = $"Imported {imported} contacts" });
    }

    private async Task<NewsletterList> FindListAsync(int id, CancellationToken ct) =>
        await _db.NewsletterLists.SingleOrDefaultAsync(l => l.Id == id, ct)
        ?? throw new InvalidOperationException($"Newsletter list #{id} not found");

    private static void AssertStatic(NewsletterList list)
    {
        if (list.Kind != "static")
            throw new InvalidOperationException("Dynamic lists are filter-driven");
    }

    private async Task<object> WithCountsAsync(NewsletterList list, CancellationToken ct)
    {
        var memberCount = await _db.NewsletterListMembers.CountAsync(m => m.ListId == list.Id, ct);
        var optedOut = await _db.NewsletterListMembers
            .Where(m => m.ListId == list.Id)
            .Join(_db.Contacts, m => m.ContactId, c => c.Id, (m, c) => c)
            .CountAsync(c => c.MarketingOptOutAt != null, ct);

        return new
        {
            list.Id,
            list.Name,
            list.Description,
            list.Kind,
            list.FilterJson,
            list.CreatedBy,
            list.CreatedAt,
            list.UpdatedAt,
            member_count = memberCount,
            opted_out_count = optedOut,
        };
    }

    private string? UserId() => User.FindFirst("sub")?.Value ?? User.FindFirst("id")?.Value ?? User.Identity?.Name;
}
