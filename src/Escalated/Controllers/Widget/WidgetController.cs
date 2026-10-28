using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Escalated.Configuration;
using Escalated.Data;
using Escalated.Enums;
using Escalated.Services;
using Escalated.Widget;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Escalated.Controllers.Widget;

/// <summary>
/// Public API endpoints for the embeddable widget.
/// Supports KB search, anonymous ticket creation, and ticket status lookup by guest token.
/// Guest ticket creation and guest replies are rate-limited per client IP
/// (see <see cref="Configuration.GuestRateLimitOptions"/>).
/// </summary>
[ApiController]
[Route("support/widget")]
[AllowAnonymous]
[WidgetEnabled]
public class WidgetController : ControllerBase
{
    private readonly TicketService _ticketService;
    private readonly KnowledgeBaseService _kbService;
    private readonly EscalatedDbContext _db;

    public WidgetController(TicketService ticketService, KnowledgeBaseService kbService, EscalatedDbContext db)
    {
        _ticketService = ticketService;
        _kbService = kbService;
        _db = db;
    }

    /// <summary>
    /// What the widget needs before it renders. While the <c>widget_enabled</c>
    /// setting is off every widget route answers 403 (<c>{enabled: false}</c>);
    /// colour, position and greeting come from the <c>widget_color</c>,
    /// <c>widget_position</c> and <c>widget_greeting</c> settings,
    /// <c>guest_tickets_enabled</c> from that setting, and <c>kb_enabled</c> from
    /// <see cref="KnowledgeBaseOptions.Enabled"/>. Guests verify their email
    /// (<c>guest_verification_required</c>), which is what lets the Status tab
    /// look tickets up (<c>status_lookup_enabled</c>).
    /// </summary>
    [HttpGet("config")]
    public async Task<IActionResult> Config(
        [FromServices] SettingsService settings,
        [FromServices] IOptions<EscalatedOptions> options,
        CancellationToken ct)
    {
        var departments = await _db.Departments
            .Where(d => d.IsActive)
            .OrderBy(d => d.Name)
            .Select(d => new { id = d.Id, name = d.Name })
            .ToListAsync(ct);

        return Ok(new
        {
            enabled = true,
            color = await settings.GetAsync("widget_color", "#4F46E5", ct),
            position = await settings.GetAsync("widget_position", "bottom-right", ct),
            greeting = await settings.GetAsync("widget_greeting", "Hi there! How can we help?", ct),
            departments,
            kb_enabled = options.Value.KnowledgeBase.Enabled,
            guest_tickets_enabled = await GuestSettings.IsOnAsync(settings, GuestSettings.GuestTicketsEnabled, ct),
            guest_verification_required = true,
            // Tickets and chats keep host parameters sent as `metadata`.
            accepts_metadata = true,
            // Lookups need a verified email (POST lookup), never a claimed one.
            status_lookup_enabled = true,
        });
    }

    /// <summary>
    /// Search published knowledge base articles.
    /// </summary>
    [HttpGet("kb/search")]
    public async Task<IActionResult> KbSearch([FromQuery] string q)
    {
        if (string.IsNullOrWhiteSpace(q))
            return Ok(Array.Empty<object>());

        var articles = await _kbService.SearchAsync(q);
        return Ok(articles.Select(a => new
        {
            a.Id,
            a.Title,
            a.Slug,
            category = a.Category?.Name,
            snippet = a.Body?.Length > 200 ? a.Body[..200] + "..." : a.Body
        }));
    }

    /// <summary>
    /// Knowledge base search in the shape the shared widget reads. Queries
    /// shorter than two characters find nothing.
    /// </summary>
    [HttpGet("articles")]
    public async Task<IActionResult> Articles(
        [FromQuery] string? q,
        [FromServices] IOptions<EscalatedOptions> options,
        CancellationToken ct)
    {
        if (!options.Value.KnowledgeBase.Enabled) return NotFound();

        var query = (q ?? string.Empty).Trim();
        if (query.Length > 200) query = query[..200];
        if (query.Length < 2) return Ok(new { articles = Array.Empty<object>() });

        var articles = await _kbService.SearchAsync(query, ct);
        return Ok(new
        {
            articles = articles.Select(a => new { title = a.Title, slug = a.Slug, excerpt = Excerpt(a.Body) }),
        });
    }

    /// <summary>
    /// One published article, in the shape the shared widget reads.
    /// </summary>
    [HttpGet("articles/{slug}")]
    public async Task<IActionResult> Article(
        string slug,
        [FromServices] IOptions<EscalatedOptions> options,
        CancellationToken ct)
    {
        if (!options.Value.KnowledgeBase.Enabled) return NotFound();

        var article = await _db.Articles
            .Include(a => a.Category)
            .FirstOrDefaultAsync(a => a.Slug == slug && a.Status == "published", ct);
        if (article == null) return NotFound(new { message = "Article not found." });

        await _kbService.RecordViewAsync(article.Id, ct);

        return Ok(new
        {
            title = article.Title,
            slug = article.Slug,
            body = article.Body ?? string.Empty,
            category = article.Category?.Name,
        });
    }

    /// <summary>Plain text from an article body, cut to 200 characters.</summary>
    private static string Excerpt(string? body)
    {
        if (string.IsNullOrEmpty(body)) return string.Empty;
        var text = WebUtility.HtmlDecode(Regex.Replace(body, "<[^>]*>", " "));
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return text.Length > 200 ? text[..200] + "..." : text;
    }

    /// <summary>Body: <c>{email, purpose: ticket|chat|lookup}</c>. Emails a code; answers 202 <c>{verification_id, expires_in, message}</c>.</summary>
    [HttpPost("verification")]
    [GuestThrottle(GuestThrottleScope.Verification)]
    public Task<IActionResult> Verification([FromBody] GuestVerificationRequest request) =>
        GuestAccessEndpoints.VerificationAsync(this, request);

    /// <summary>Body: <c>{email, reference, verification_id, verification_code}</c>. Answers <c>{data: [{reference, subject, guest_access_token, expires_at}]}</c>.</summary>
    [HttpPost("lookup")]
    [GuestThrottle(GuestThrottleScope.Verification)]
    public Task<IActionResult> Lookup([FromBody] GuestLookupRequest request) =>
        GuestAccessEndpoints.LookupAsync(this, request);

    /// <summary>
    /// Create a guest ticket. The guest proves the email with a <c>ticket</c> code
    /// (<c>verification_id</c>, <c>verification_code</c>) and gets an expiring access
    /// grant: 201 <c>{message, reference, guest_access_token, expires_at}</c>.
    /// </summary>
    [HttpPost("tickets")]
    [GuestThrottle(GuestThrottleScope.Ticket)]
    public async Task<IActionResult> CreateGuestTicket(
        [FromBody] GuestTicketRequest request,
        [FromServices] SettingsService settings,
        [FromServices] GuestAccessService access,
        [FromServices] IOptions<EscalatedOptions> options)
    {
        var ct = HttpContext.RequestAborted;
        GuestAccessEndpoints.NoStore(Response);

        if (!await GuestSettings.IsOnAsync(settings, GuestSettings.GuestTicketsEnabled, ct))
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Guest tickets are disabled." });

        var invalid = WidgetMetadata.Validate(request.Metadata);
        if (invalid is not null)
        {
            ModelState.AddModelError("metadata", invalid);
            return ValidationProblem(ModelState);
        }

        var departmentId = request.WidgetDepartmentId ?? request.DepartmentId;
        if (!await WidgetDepartments.IsAllowedAsync(_db, departmentId, ct))
        {
            ModelState.AddModelError(WidgetDepartments.Field, WidgetDepartments.Invalid);
            return ValidationProblem(ModelState);
        }

        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 255) errors["name"] = new[] { "Your name is required." };
        if (GuestAccessService.NormalizeEmail(request.Email) is null) errors["email"] = new[] { "A valid email address is required." };
        if (string.IsNullOrWhiteSpace(request.Subject) || request.Subject.Length > 255) errors["subject"] = new[] { "The subject is required (at most 255 characters)." };
        if (string.IsNullOrWhiteSpace(request.Description) || request.Description.Length > 5000) errors["description"] = new[] { "The description is required (at most 5000 characters)." };
        if (string.IsNullOrEmpty(request.VerificationId)) errors["verification_id"] = new[] { "Request a verification code first." };
        if (string.IsNullOrEmpty(request.VerificationCode)) errors["verification_code"] = new[] { "Enter the code from your email." };
        if (errors.Count > 0) return GuestAccessEndpoints.Invalid(this, errors);

        if (!await access.ConsumeAsync(request.VerificationId, request.VerificationCode, request.Email, "ticket", ct))
            return GuestAccessEndpoints.InvalidCode(this);

        Models.Ticket ticket;
        string token;
        DateTime expires;
        try
        {
            ticket = await _ticketService.CreateAsync(
                request.Subject.Trim(),
                request.Description,
                guestName: request.Name!.Trim(),
                guestEmail: request.Email!.Trim(),
                departmentId: departmentId,
                metadata: WidgetMetadata.Sanitize(request.Metadata, HttpContext.Items[WidgetOrigins.ItemKey] as string));
            (token, expires) = await access.IssueGrantAsync(ticket, ct);
        }
        catch
        {
            await access.ReleaseAsync(request.VerificationId!, CancellationToken.None);
            throw;
        }

        var body = new Dictionary<string, object?>
        {
            ["message"] = "Ticket created successfully.",
            ["reference"] = ticket.Reference,
            ["guest_access_token"] = token,
            ["expires_at"] = expires,
        };

        // During the transition, older widgets keep getting the permanent token they used.
        if (options.Value.GuestAccess.LegacyTokens) body["guestToken"] = ticket.GuestToken;

        return StatusCode(StatusCodes.Status201Created, body);
    }

    /// <summary>
    /// A guest ticket's status, as the widget's Status tab reads it. Needs
    /// <c>Authorization: Bearer &lt;guest_access_token&gt;</c> for this
    /// <paramref name="reference"/>; with <see cref="GuestAccessOptions.LegacyTokens"/>
    /// the segment may instead be a permanent guest token from before.
    /// </summary>
    [HttpGet("tickets/{reference}")]
    [GuestThrottle(GuestThrottleScope.Request)]
    public async Task<IActionResult> TicketStatus(string reference, [FromServices] GuestAccessService access)
    {
        GuestAccessEndpoints.NoStore(Response);
        var ticket = await GuestTicketAsync(reference, access, HttpContext.RequestAborted);
        if (ticket == null) return NotFound(new { message = "Ticket not found." });

        await _db.Entry(ticket).Reference(t => t.Department).LoadAsync();
        var replies = await _db.Replies
            .Where(r => r.TicketId == ticket.Id && !r.IsInternalNote && (r.Type == null || r.Type != "note"))
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .Take(20)
            .ToListAsync();

        return Ok(new
        {
            reference = ticket.Reference,
            subject = ticket.Subject,
            status = ticket.Status.ToValue(),
            status_label = ticket.Status.Label(),
            status_color = ticket.Status.Color(),
            created_at = ticket.CreatedAt,
            department = ticket.Department?.Name,
            replies = replies.Select(r =>
            {
                var isAgent = IsStaff(r, ticket);
                return new
                {
                    body = r.Body,
                    author = isAgent ? "Support" : ticket.GuestName ?? "You",
                    is_agent = isAgent,
                    created_at = r.CreatedAt,
                };
            }),
        });
    }

    /// <summary>A guest's reply on their ticket; the same access as <see cref="TicketStatus"/>.</summary>
    [HttpPost("tickets/{reference}/reply")]
    [HttpPost("tickets/{reference}/replies")]
    // Throttled before the grant is checked, so guesses are counted too.
    [GuestThrottle(GuestThrottleScope.Reply)]
    public async Task<IActionResult> GuestReply(string reference, [FromBody] GuestReplyRequest request, [FromServices] GuestAccessService access)
    {
        GuestAccessEndpoints.NoStore(Response);
        var ticket = await GuestTicketAsync(reference, access, HttpContext.RequestAborted);
        if (ticket == null) return NotFound(new { message = "Ticket not found." });
        if (string.IsNullOrWhiteSpace(request.Body) || request.Body.Length > 10000)
            return GuestAccessEndpoints.Invalid(this, new Dictionary<string, string[]> { ["body"] = new[] { "The reply is required." } });

        var reply = await _ticketService.AddReplyAsync(ticket, request.Body);
        return StatusCode(StatusCodes.Status201Created, new { id = reply.Id, body = reply.Body, created_at = reply.CreatedAt });
    }

    /// <summary>
    /// Submit a satisfaction rating for a ticket; the same access as <see cref="TicketStatus"/>.
    /// </summary>
    [HttpPost("tickets/{reference}/rate")]
    [GuestThrottle(GuestThrottleScope.Reply)]
    public async Task<IActionResult> Rate(string reference, [FromBody] RateRequest request, [FromServices] GuestAccessService access)
    {
        GuestAccessEndpoints.NoStore(Response);
        var ticket = await GuestTicketAsync(reference, access, HttpContext.RequestAborted);
        if (ticket == null) return NotFound(new { message = "Ticket not found." });

        if (request.Rating is < 1 or > 5)
            return GuestAccessEndpoints.Invalid(this, new Dictionary<string, string[]> { ["rating"] = new[] { "The rating must be from 1 to 5." } });

        var existing = await _db.SatisfactionRatings.FirstOrDefaultAsync(r => r.TicketId == ticket.Id);
        if (existing != null)
            return BadRequest(new { message = "Rating already submitted." });

        var rating = new Models.SatisfactionRating
        {
            TicketId = ticket.Id,
            Rating = request.Rating,
            Comment = request.Comment,
            CreatedAt = DateTime.UtcNow
        };
        _db.SatisfactionRatings.Add(rating);
        await _db.SaveChangesAsync();

        return Ok(new { message = "Rating submitted.", rating = rating.Rating });
    }

    /// <summary>
    /// The ticket a guest request may act on: the bearer grant must be this
    /// reference's unexpired grant. With legacy tokens on, a permanent guest token
    /// in the URL segment is still accepted.
    /// </summary>
    private async Task<Models.Ticket?> GuestTicketAsync(string reference, GuestAccessService access, CancellationToken ct)
    {
        var header = Request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = header["Bearer ".Length..].Trim();
            if (token.Length is 0 or > 256) return null;
            var ticket = await _db.Tickets.Where(access.Opens(token)).FirstOrDefaultAsync(ct);
            return ticket is not null && ticket.Reference == reference && ticket.GuestAccessHash == GuestAccessService.HashGrant(token)
                ? ticket
                : null;
        }

        if (string.IsNullOrEmpty(reference) || reference.Length > 128) return null;
        var legacy = await _db.Tickets.Where(access.Opens(reference)).FirstOrDefaultAsync(ct);
        return legacy is not null && legacy.GuestToken == reference ? legacy : null;
    }

    /// <summary>Replies by someone other than the guest are from support.</summary>
    private static bool IsStaff(Models.Reply reply, Models.Ticket ticket) =>
        !string.IsNullOrEmpty(reply.AuthorId) && reply.AuthorType != "visitor" && reply.AuthorId != ticket.RequesterId;

    /// <summary>
    /// Record feedback (helpful/not helpful) on a KB article.
    /// </summary>
    [HttpPost("kb/articles/{id:int}/feedback")]
    public async Task<IActionResult> ArticleFeedback(int id, [FromBody] FeedbackRequest request)
    {
        await _kbService.RecordFeedbackAsync(id, request.Helpful);
        return Ok(new { message = "Feedback recorded." });
    }
}

/// <param name="Metadata">Optional host parameters; see <see cref="WidgetMetadata"/>.</param>
public record GuestTicketRequest(string Subject, string? Description, string? Name, string? Email, int? DepartmentId = null, JsonElement? Metadata = null)
{
    [JsonPropertyName("verification_id")]
    public string? VerificationId { get; init; }

    [JsonPropertyName("verification_code")]
    public string? VerificationCode { get; init; }

    /// <summary>The shared widget's <c>department_id</c>; wins over <c>departmentId</c>.</summary>
    [JsonPropertyName("department_id")]
    public int? WidgetDepartmentId { get; init; }
}
public record GuestReplyRequest(string? Body);
public record RateRequest(int Rating, string? Comment = null);
public record FeedbackRequest(bool Helpful);
