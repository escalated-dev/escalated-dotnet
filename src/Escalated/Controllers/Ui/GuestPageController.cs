using Escalated.Configuration;
using Escalated.Controllers.Widget;
using Escalated.Data;
using Escalated.Enums;
using Escalated.Extensions;
using Escalated.Models;
using Escalated.Services;
using Escalated.Services.Email.Inbound;
using Escalated.Ui;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Escalated.Controllers.Ui;

/// <summary>
/// The guest pages (<c>Escalated/Guest/Create</c> and <c>Guest/Show</c>) on the
/// verified guest flow, with escalated-laravel's <c>Guest/TicketController</c> routes
/// and props. A guest proves their email with a <c>ticket</c> code
/// (<c>POST support/guest/verification</c>) and gets a private link carrying an
/// expiring access grant; the link's token opens the ticket, its replies and its
/// files until the grant expires. See <c>docs/guest-access.md</c>.
/// </summary>
[Route("support/guest")]
[AllowAnonymous]
[EscalatedPage]
public class GuestPageController : ControllerBase
{
    /// <summary>A grant token as escalated-laravel's route accepts it; literal segments (<c>create</c>) take precedence.</summary>
    private const string Token = "{token:regex(^[[A-Za-z0-9_-]]{{1,4096}}$)}";

    private static readonly HashSet<string> Priorities = Enum.GetValues<TicketPriority>().Select(p => p.ToValue()).ToHashSet();

    private readonly EscalatedDbContext _db;
    private readonly TicketService _tickets;
    private readonly GuestAccessService _access;
    private readonly SettingsService _settings;
    private readonly UiPresenter _present;
    private readonly IAttachmentStorage _storage;
    private readonly EscalatedOptions _options;

    public GuestPageController(EscalatedDbContext db, TicketService tickets, GuestAccessService access, SettingsService settings,
        UiPresenter present, IAttachmentStorage storage, IOptions<EscalatedOptions> options)
    {
        _db = db;
        _tickets = tickets;
        _access = access;
        _settings = settings;
        _present = present;
        _storage = storage;
        _options = options.Value;
    }

    private string Prefix => "/" + _options.RoutePrefix.Trim('/');

    private string ShowUrl(string token) => $"{Prefix}/guest/{token}";

    private Task<bool> EnabledAsync(CancellationToken ct) => GuestSettings.IsOnAsync(_settings, GuestSettings.GuestTicketsEnabled, ct);

    /// <summary>The ticket the token's unexpired grant opens, or null.</summary>
    private async Task<Ticket?> ResolveAsync(string token, CancellationToken ct)
    {
        var ticket = await _db.Tickets.Where(_access.Opens(token)).FirstOrDefaultAsync(ct);
        if (ticket is null) return null;
        return ticket.GuestAccessHash == GuestAccessService.HashGrant(token)
               || (_options.GuestAccess.LegacyTokens && ticket.GuestToken == token)
            ? ticket
            : null;
    }

    [HttpGet("create")]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        if (!await EnabledAsync(ct)) return NotFound();
        GuestAccessEndpoints.NoStore(Response);

        return UiResults.Render("Escalated/Guest/Create", new
        {
            departments = await _present.DepartmentOptionsAsync(ct),
            priorities = Enum.GetValues<TicketPriority>().Select(p => p.ToValue()).ToList(),
            verification_url = $"{Prefix}/guest/verification",
            lookup_url = $"{Prefix}/guest/lookup",
        });
    }

    [HttpPost("")]
    [GuestThrottle(GuestThrottleScope.Ticket)]
    public async Task<IActionResult> Store(CancellationToken ct)
    {
        if (!await EnabledAsync(ct)) return NotFound();
        GuestAccessEndpoints.NoStore(Response);

        var input = await UiInput.ReadAsync(Request, ct);
        var name = input.String("guest_name")?.Trim();
        var email = input.String("guest_email")?.Trim();
        var subject = input.String("subject")?.Trim();
        var description = input.String("description");
        var priority = input.String("priority");
        var departmentId = input.Int("department_id");
        var verificationId = input.String("verification_id");
        var verificationCode = input.String("verification_code");

        var errors = new Dictionary<string, string>();
        if (name is null) errors["guest_name"] = "The name field is required.";
        else if (name.Length > 255) errors["guest_name"] = "The name may not be greater than 255 characters.";
        if (email is null || email.Length > 255 || GuestAccessService.NormalizeEmail(email) is null) errors["guest_email"] = "A valid email address is required.";
        if (subject is null) errors["subject"] = "The subject field is required.";
        else if (subject.Length > 255) errors["subject"] = "The subject may not be greater than 255 characters.";
        if (description is null) errors["description"] = "The description field is required.";
        else if (description.Length > 65535) errors["description"] = "The description is too long.";
        if (verificationId is null || !Guid.TryParse(verificationId, out _)) errors["verification_id"] = "Request a verification code first.";
        if (verificationCode is null || verificationCode.Length > 16) errors["verification_code"] = "Enter the code from your email.";
        if (priority is not null && !Priorities.Contains(priority)) errors["priority"] = "The selected priority is invalid.";
        if (input.String("department_id") is not null
            && (departmentId is null || !await _db.Departments.AnyAsync(d => d.Id == departmentId && d.IsActive, ct)))
        {
            errors["department_id"] = "The selected department is invalid.";
        }
        if (UiAttachments.Validate(input.Files, _options.Attachments) is { } attachmentError) errors["attachments"] = attachmentError;

        var formUrl = $"{Prefix}/guest/create";
        if (errors.Count > 0) return UiResults.Back(HttpContext, formUrl, errors: errors);

        if (!await _access.ConsumeAsync(verificationId, verificationCode, email, "ticket", ct))
        {
            return UiResults.Back(HttpContext, formUrl,
                errors: new Dictionary<string, string> { ["verification_code"] = "The verification code is invalid or has expired." });
        }

        string token;
        try
        {
            var ticket = await _tickets.CreateAsync(subject!, description,
                priority: priority is null ? null : TicketPriorityExtensions.Parse(priority),
                departmentId: departmentId, guestName: name, guestEmail: email, ct: ct);
            await UiAttachments.StoreAsync(_db, _storage, input.Files, "ticket", ticket.Id, ct);
            (token, _) = await _access.IssueGrantAsync(ticket, ct);
        }
        catch
        {
            await _access.ReleaseAsync(verificationId!, CancellationToken.None);
            throw;
        }

        return new UiRedirectResult(ShowUrl(token)) { Success = "Ticket created." };
    }

    [HttpGet(Token)]
    public async Task<IActionResult> Show(string token, CancellationToken ct)
    {
        GuestAccessEndpoints.NoStore(Response);
        var found = await ResolveAsync(token, ct);
        if (found is null) return NotFound();

        var ticket = await _db.Tickets
            .Include(t => t.Replies.Where(r => !r.IsInternalNote).OrderByDescending(r => r.CreatedAt))
            .Include(t => t.Department)
            .Include(t => t.SatisfactionRating)
            .Include(t => t.ChatSessions)
            .AsSplitQuery()
            .FirstAsync(t => t.Id == found.Id, ct);
        await _db.LoadAttachmentsAsync(ticket, ct);
        foreach (var attachment in ticket.Attachments.Concat(ticket.Replies.SelectMany(r => r.Attachments)))
        {
            attachment.Url = $"{Request.Scheme}://{Request.Host}{ShowUrl(token)}/attachments/{attachment.Id}";
        }

        var payload = await _present.CustomerTicketAsync(ticket, withThread: true, ct);
        var replies = ((List<Dictionary<string, object?>>)payload["replies"]!).Select(reply =>
        {
            var author = (Dictionary<string, object?>)reply["author"]!;
            reply["author"] = new Dictionary<string, object?> { ["name"] = author["name"] };
            return reply;
        }).ToList();

        return UiResults.Render("Escalated/Guest/Show", new
        {
            ticket = new Dictionary<string, object?>
            {
                ["reference"] = payload["reference"],
                ["subject"] = payload["subject"],
                ["description"] = payload["description"],
                ["status"] = payload["status"],
                ["priority"] = payload["priority"],
                ["channel"] = payload["channel"],
                ["department"] = payload["department"],
                ["attachments"] = payload["attachments"],
                ["replies"] = replies,
                ["satisfaction_rating"] = payload["satisfaction_rating"],
                ["guest_access_expires_at"] = ticket.GuestAccessExpiresAt,
                ["resolved_at"] = ticket.ResolvedAt,
                ["closed_at"] = ticket.ClosedAt,
                ["created_at"] = ticket.CreatedAt,
                ["updated_at"] = ticket.UpdatedAt,
            },
            token,
        });
    }

    [HttpPost(Token + "/reply")]
    [GuestThrottle(GuestThrottleScope.Reply)]
    public async Task<IActionResult> Reply(string token, CancellationToken ct)
    {
        GuestAccessEndpoints.NoStore(Response);
        var ticket = await ResolveAsync(token, ct);
        if (ticket is null) return NotFound();
        if (ticket.Status == TicketStatus.Closed) return UiResults.Back(HttpContext, ShowUrl(token), error: "This ticket is closed.");

        var input = await UiInput.ReadAsync(Request, ct);
        var body = input.String("body");
        var errors = new Dictionary<string, string>();
        if (body is null) errors["body"] = "The body field is required.";
        else if (body.Length > 65535) errors["body"] = "The reply is too long.";
        if (UiAttachments.Validate(input.Files, _options.Attachments) is { } attachmentError) errors["attachments"] = attachmentError;
        if (errors.Count > 0) return UiResults.Back(HttpContext, ShowUrl(token), errors: errors);

        var reply = await _tickets.AddReplyAsync(ticket, body!, null, null, isNote: false, ct);
        await UiAttachments.StoreAsync(_db, _storage, input.Files, "reply", reply.Id, ct);

        return UiResults.Back(HttpContext, ShowUrl(token), success: "Reply sent.");
    }

    /// <summary>A 1-5 rating with an optional comment, once, on a resolved or closed ticket.</summary>
    [HttpPost(Token + "/rate")]
    [GuestThrottle(GuestThrottleScope.Reply)]
    public async Task<IActionResult> Rate(string token, CancellationToken ct)
    {
        GuestAccessEndpoints.NoStore(Response);
        var ticket = await ResolveAsync(token, ct);
        if (ticket is null) return NotFound();

        var input = await UiInput.ReadAsync(Request, ct);
        var rating = input.Int("rating");
        var comment = input.String("comment");
        var errors = new Dictionary<string, string>();
        if (rating is not (>= 1 and <= 5)) errors["rating"] = "The rating must be between 1 and 5.";
        if (comment is { Length: > 2000 }) errors["comment"] = "The comment may not be greater than 2000 characters.";
        if (errors.Count > 0) return UiResults.Back(HttpContext, ShowUrl(token), errors: errors);

        if (ticket.Status is not (TicketStatus.Resolved or TicketStatus.Closed))
        {
            return UiResults.Back(HttpContext, ShowUrl(token), error: "Only resolved or closed tickets can be rated.");
        }

        if (await _db.SatisfactionRatings.AnyAsync(r => r.TicketId == ticket.Id, ct))
        {
            return UiResults.Back(HttpContext, ShowUrl(token), error: "This ticket has already been rated.");
        }

        _db.SatisfactionRatings.Add(new SatisfactionRating
        {
            TicketId = ticket.Id,
            Rating = rating!.Value,
            Comment = comment,
            RaterType = "guest",
            CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync(ct);

        return UiResults.Back(HttpContext, ShowUrl(token), success: "Thank you for your feedback.");
    }

    /// <summary>
    /// A file on the ticket or on one of its public replies, for the holder of the
    /// ticket's grant. Files on internal notes, and on other tickets, are a 404.
    /// </summary>
    [HttpGet(Token + "/attachments/{id:int}")]
    public async Task<IActionResult> Attachment(string token, int id, CancellationToken ct)
    {
        GuestAccessEndpoints.NoStore(Response);
        var ticket = await ResolveAsync(token, ct);
        if (ticket is null) return NotFound();

        var attachment = await _db.Attachments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (attachment is null) return NotFound();

        var belongs = attachment.AttachableType.EndsWith("ticket", StringComparison.OrdinalIgnoreCase)
            ? attachment.AttachableId == ticket.Id
            : attachment.AttachableType.EndsWith("reply", StringComparison.OrdinalIgnoreCase)
              && await _db.Replies.AnyAsync(r => r.Id == attachment.AttachableId && r.TicketId == ticket.Id && !r.IsInternalNote, ct);
        if (!belongs) return NotFound();

        if (attachment.Disk == "local")
        {
            if (!System.IO.File.Exists(attachment.Path)) return NotFound();
            return File(System.IO.File.OpenRead(attachment.Path), attachment.MimeType, attachment.Filename);
        }

        return Uri.TryCreate(attachment.Path, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? Redirect(attachment.Path)
            : NotFound();
    }
}
