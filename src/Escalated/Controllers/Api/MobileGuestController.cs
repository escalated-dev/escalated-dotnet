using Escalated.Configuration;
using Escalated.Controllers.Widget;
using Escalated.Data;
using Escalated.Enums;
using Escalated.Models;
using Escalated.Services;
using Escalated.Services.Email.Inbound;
using Escalated.Ui;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Escalated.Controllers.Api;

/// <summary>
/// The mobile guest API (<c>support/api/v1/mobile/guest</c>): a guest with no
/// account files a ticket, reads it, replies to it and gets back into it, on the
/// same email codes, expiring grants and per-IP guest budgets as the widget and
/// the guest pages. Canonical contract:
/// https://github.com/escalated-dev/escalated-developer-context/blob/main/domain-model/mobile-guest-api.md
/// (escalated-laravel's <c>MobileGuestTicketController</c>). See <c>docs/guest-access.md</c>.
/// </summary>
[ApiController]
[Route(RoutePrefix)]
[AllowAnonymous]
[MobileGuestPrivacyHeaders]
public class MobileGuestController : ControllerBase
{
    public const string RoutePrefix = "support/api/v1/mobile/guest";

    private const int MaxAttachments = 10;
    private static readonly HashSet<string> Priorities = Enum.GetValues<TicketPriority>().Select(p => p.ToValue()).ToHashSet();

    private readonly EscalatedDbContext _db;
    private readonly TicketService _tickets;
    private readonly GuestAccessService _access;
    private readonly SettingsService _settings;
    private readonly UiPresenter _present;
    private readonly IAttachmentStorage _storage;
    private readonly EscalatedOptions _options;

    public MobileGuestController(EscalatedDbContext db, TicketService tickets, GuestAccessService access, SettingsService settings,
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

    /// <summary>Body: <c>{email, purpose: ticket|chat|lookup}</c>. The widget's and guest pages' handler.</summary>
    [HttpPost("verification")]
    [GuestThrottle(GuestThrottleScope.Verification)]
    public Task<IActionResult> Verification([FromBody] GuestVerificationRequest request) =>
        GuestAccessEndpoints.VerificationAsync(this, request);

    /// <summary>Body: <c>{email, reference, verification_id, verification_code}</c>. The widget's and guest pages' handler.</summary>
    [HttpPost("lookup")]
    [GuestThrottle(GuestThrottleScope.Verification)]
    public Task<IActionResult> Lookup([FromBody] GuestLookupRequest request) =>
        GuestAccessEndpoints.LookupAsync(this, request);

    /// <summary>
    /// Creates a ticket from JSON or <c>multipart/form-data</c> with a <c>ticket</c>
    /// code. 201 <c>{data: &lt;mobile ticket&gt;, message}</c> carrying a fresh grant.
    /// </summary>
    [HttpPost("tickets")]
    [GuestThrottle(GuestThrottleScope.Ticket)]
    public async Task<IActionResult> Store(CancellationToken ct)
    {
        if (!await GuestSettings.IsOnAsync(_settings, GuestSettings.GuestTicketsEnabled, ct))
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Guest tickets are not enabled." });

        var input = await UiInput.ReadAsync(Request, ct);
        var name = input.String("name")?.Trim();
        var email = input.String("email")?.Trim();
        var subject = input.String("subject")?.Trim();
        var description = input.String("description");
        var priority = input.String("priority");
        var departmentId = input.Int("department_id");
        var verificationId = input.String("verification_id");
        var verificationCode = input.String("verification_code");

        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrEmpty(name) || name.Length > 255) errors["name"] = new[] { "The name is required (at most 255 characters)." };
        if (email is null || GuestAccessService.NormalizeEmail(email) is null) errors["email"] = new[] { "A valid email address is required." };
        if (string.IsNullOrEmpty(subject) || subject.Length > 255) errors["subject"] = new[] { "The subject is required (at most 255 characters)." };
        if (string.IsNullOrWhiteSpace(description) || description.Length > 65535) errors["description"] = new[] { "The description is required (at most 65535 characters)." };
        if (verificationId is null || !Guid.TryParse(verificationId, out _)) errors["verification_id"] = new[] { "Request a verification code first." };
        if (string.IsNullOrEmpty(verificationCode) || verificationCode.Length > 16) errors["verification_code"] = new[] { "Enter the code from your email." };
        if (priority is not null && !Priorities.Contains(priority)) errors["priority"] = new[] { "The selected priority is invalid." };
        if (input.String("department_id") is not null
            && (departmentId is null || !await _db.Departments.AnyAsync(d => d.Id == departmentId, ct)))
        {
            errors["department_id"] = new[] { "The selected department is invalid." };
        }
        if (AttachmentError(input.Files) is { } attachmentError) errors["attachments"] = new[] { attachmentError };
        if (errors.Count > 0) return GuestAccessEndpoints.Invalid(this, errors);

        // Nothing is created without a valid, unspent `ticket` code for this email.
        if (!await _access.ConsumeAsync(verificationId, verificationCode, email, "ticket", ct))
            return GuestAccessEndpoints.InvalidCode(this);

        Ticket ticket;
        string token;
        try
        {
            // Status, assignee, metadata and external references are never taken from a guest.
            ticket = await _tickets.CreateAsync(subject!, description,
                priority: priority is null ? null : TicketPriorityExtensions.Parse(priority),
                departmentId: departmentId,
                guestName: name,
                guestEmail: GuestAccessService.NormalizeEmail(email),
                ct: ct);
            await UiAttachments.StoreAsync(_db, _storage, input.Files, "ticket", ticket.Id, ct);
            (token, _) = await _access.IssueGrantAsync(ticket, ct);
        }
        catch
        {
            // The code is handed back, so the guest can retry.
            await _access.ReleaseAsync(verificationId!, CancellationToken.None);
            throw;
        }

        return StatusCode(StatusCodes.Status201Created, new
        {
            data = await MobileTicketAsync(ticket.Id, token, ct),
            message = "Ticket created.",
        });
    }

    /// <summary>The ticket the grant opens, with its public replies newest first. A bad grant is a 404.</summary>
    [HttpGet("tickets/{token}")]
    [GuestThrottle(GuestThrottleScope.Request)]
    public async Task<IActionResult> Show(string token, CancellationToken ct)
    {
        var ticket = await _access.ResolveTicketAsync(token, ct);
        if (ticket is null) return GrantNotFound();

        return Ok(new { data = await MobileTicketAsync(ticket.Id, token, ct) });
    }

    /// <summary>
    /// A public reply from the guest. Body <c>{email, body}</c> plus optional
    /// <c>attachments[]</c>; the email must be the ticket's guest email.
    /// </summary>
    [HttpPost("tickets/{token}/replies")]
    [GuestThrottle(GuestThrottleScope.Reply)]
    public async Task<IActionResult> Reply(string token, CancellationToken ct)
    {
        var ticket = await _access.ResolveTicketAsync(token, ct);
        if (ticket is null) return GrantNotFound();
        if (ticket.Status == TicketStatus.Closed)
            return UnprocessableEntity(new { message = "This ticket is closed." });

        var input = await UiInput.ReadAsync(Request, ct);
        var body = input.String("body");
        var email = GuestAccessService.NormalizeEmail(input.String("email"));

        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(body) || body.Length > 65535) errors["body"] = new[] { "The reply is required (at most 65535 characters)." };
        if (email is null) errors["email"] = new[] { "A valid email address is required." };
        if (AttachmentError(input.Files) is { } attachmentError) errors["attachments"] = new[] { attachmentError };
        if (errors.Count > 0) return GuestAccessEndpoints.Invalid(this, errors);

        // A second factor on top of the grant, not a substitute for it.
        if (email != GuestAccessService.NormalizeEmail(ticket.GuestEmail))
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "The provided email does not match this ticket." });

        var reply = await _tickets.AddReplyAsync(ticket, body!, null, null, isNote: false, ct);
        await UiAttachments.StoreAsync(_db, _storage, input.Files, "reply", reply.Id, ct);

        return StatusCode(StatusCodes.Status201Created, new { message = "Reply sent." });
    }

    /// <summary>A file on the ticket or on one of its public replies, for the holder of the grant.</summary>
    [HttpGet("tickets/{token}/attachments/{id:int}")]
    [GuestThrottle(GuestThrottleScope.Request)]
    public async Task<IActionResult> Attachment(string token, int id, CancellationToken ct)
    {
        var ticket = await _access.ResolveTicketAsync(token, ct);
        if (ticket is null) return GrantNotFound();

        return await GuestAccessEndpoints.AttachmentAsync(this, _db, ticket, id, ct);
    }

    private IActionResult GrantNotFound() => NotFound(new { message = "Ticket not found." });

    private string? AttachmentError(IFormFileCollection? files) =>
        files is { Count: > MaxAttachments }
            ? $"No more than {MaxAttachments} attachments may be added."
            : UiAttachments.Validate(files, _options.Attachments);

    /// <summary>
    /// The allow-listed mobile ticket object. The model is never serialized: no
    /// metadata, external references, internal notes or staff emails and ids.
    /// </summary>
    private async Task<Dictionary<string, object?>> MobileTicketAsync(int ticketId, string token, CancellationToken ct)
    {
        var ticket = await _db.Tickets
            .Include(t => t.Replies.Where(r => !r.IsInternalNote && (r.Type == null || r.Type != "note")).OrderByDescending(r => r.CreatedAt))
            .Include(t => t.Department)
            .Include(t => t.ChatSessions)
            .AsSplitQuery()
            .FirstAsync(t => t.Id == ticketId, ct);
        await _db.LoadAttachmentsAsync(ticket, ct);

        var baseUrl = $"{Request.Scheme}://{Request.Host}/{RoutePrefix}/tickets/{token}/attachments/";
        foreach (var attachment in ticket.Attachments.Concat(ticket.Replies.SelectMany(r => r.Attachments)))
        {
            attachment.Url = baseUrl + attachment.Id;
        }

        var payload = await _present.CustomerTicketAsync(ticket, withThread: true, ct);
        var replies = ((List<Dictionary<string, object?>>)payload["replies"]!).Select(reply => new Dictionary<string, object?>
        {
            ["id"] = reply["id"],
            ["body"] = reply["body"],
            ["is_internal_note"] = false,
            ["is_pinned"] = reply["is_pinned"],
            ["author"] = reply["author"],
            ["attachments"] = ((IEnumerable<Dictionary<string, object?>>)reply["attachments"]!).Select(a => new Dictionary<string, object?>
            {
                ["id"] = a["id"],
                ["filename"] = a["original_filename"],
                ["mime_type"] = a["mime_type"],
                ["size"] = a["size"],
                ["url"] = a["url"],
            }).ToList(),
            ["created_at"] = Utc((DateTime?)reply["created_at"]),
        }).ToList();

        return new Dictionary<string, object?>
        {
            ["id"] = ticket.Id,
            ["reference"] = ticket.Reference,
            ["guest_access_token"] = token,
            ["guest_access_expires_at"] = Utc(ticket.GuestAccessExpiresAt),
            ["subject"] = ticket.Subject,
            ["description"] = ticket.Description ?? string.Empty,
            ["status"] = new Dictionary<string, object?> { ["value"] = ticket.Status.ToValue(), ["label"] = ticket.Status.Label() },
            ["priority"] = new Dictionary<string, object?> { ["value"] = ticket.Priority.ToValue(), ["label"] = ticket.Priority.Label() },
            // No channel column: chat tickets are "chat", everything a guest files is "web".
            ["channel"] = ticket.ChatSessions.Count > 0 ? "chat" : "web",
            ["metadata"] = new Dictionary<string, object?>(),
            ["requester"] = new Dictionary<string, object?>
            {
                ["name"] = string.IsNullOrEmpty(ticket.GuestName) ? "Guest" : ticket.GuestName,
                ["email"] = ticket.GuestEmail,
            },
            ["assignee"] = payload["assignee"],
            ["department"] = ticket.Department is null
                ? null
                : new Dictionary<string, object?> { ["id"] = ticket.Department.Id, ["name"] = ticket.Department.Name },
            ["tags"] = Array.Empty<object>(),
            ["replies"] = replies,
            ["activities"] = Array.Empty<object>(),
            ["sla"] = new Dictionary<string, object?>
            {
                ["first_response_due_at"] = Utc(ticket.FirstResponseDueAt),
                ["first_response_at"] = Utc(ticket.FirstResponseAt),
                ["first_response_breached"] = ticket.SlaFirstResponseBreached,
                ["resolution_due_at"] = Utc(ticket.ResolutionDueAt),
                ["resolution_breached"] = ticket.SlaResolutionBreached,
            },
            ["is_following"] = false,
            ["followers_count"] = 0,
            ["resolved_at"] = Utc(ticket.ResolvedAt),
            ["closed_at"] = Utc(ticket.ClosedAt),
            ["created_at"] = Utc(ticket.CreatedAt),
            ["updated_at"] = Utc(ticket.UpdatedAt),
        };
    }

    /// <summary>Stored times are UTC; say so, so they serialize as ISO 8601 with a <c>Z</c>.</summary>
    private static DateTime? Utc(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Utc } utc => utc,
        { Kind: DateTimeKind.Local } local => local.ToUniversalTime(),
        var unspecified => DateTime.SpecifyKind(unspecified.Value, DateTimeKind.Utc),
    };
}

/// <summary>
/// <c>Cache-Control: private, no-store</c> and <c>Referrer-Policy: no-referrer</c>
/// on every mobile guest answer, success or error, 404s and 429s included: set as
/// the response starts, so filters that end the request early are covered too.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class MobileGuestPrivacyHeadersAttribute : Attribute, IResourceFilter, IOrderedFilter
{
    // Before the guest throttle, which can end the request.
    public int Order => int.MinValue;

    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var response = context.HttpContext.Response;
        response.OnStarting(() =>
        {
            response.Headers.CacheControl = "private, no-store";
            response.Headers["Referrer-Policy"] = "no-referrer";
            return Task.CompletedTask;
        });
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
