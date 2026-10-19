using System.Text.Json;
using Escalated.Configuration;
using Escalated.Data;
using Escalated.Enums;
using Escalated.Services;
using Escalated.Widget;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;

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
    /// What the widget needs before it renders. The widget routes are always
    /// mounted, so the widget is always enabled; colour, position and greeting
    /// come from the <c>widget_color</c>, <c>widget_position</c> and
    /// <c>widget_greeting</c> settings, and <c>kb_enabled</c> from
    /// <see cref="KnowledgeBaseOptions.Enabled"/>.
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
            guest_tickets_enabled = true,
            // Tickets and chats keep host parameters sent as `metadata`.
            accepts_metadata = true,
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
    /// Create a guest (anonymous) ticket.
    /// </summary>
    [HttpPost("tickets")]
    [GuestThrottle(GuestThrottleScope.Ticket)]
    public async Task<IActionResult> CreateGuestTicket([FromBody] GuestTicketRequest request)
    {
        var invalid = WidgetMetadata.Validate(request.Metadata);
        if (invalid is not null)
        {
            ModelState.AddModelError("metadata", invalid);
            return ValidationProblem(ModelState);
        }

        var ticket = await _ticketService.CreateAsync(
            request.Subject,
            request.Description,
            guestName: request.Name,
            guestEmail: request.Email,
            departmentId: request.DepartmentId,
            metadata: WidgetMetadata.Sanitize(request.Metadata, HttpContext.Items[WidgetOrigins.ItemKey] as string));

        return Ok(new
        {
            ticket.Id,
            ticket.Reference,
            ticket.GuestToken,
            message = "Ticket created. Save your token to check status."
        });
    }

    /// <summary>
    /// Look up a guest ticket by its magic token.
    /// </summary>
    [HttpGet("tickets/{token}")]
    public async Task<IActionResult> LookupByToken(string token)
    {
        var ticket = await _db.Tickets
            .Include(t => t.Replies.Where(r => !r.IsInternalNote).OrderByDescending(r => r.CreatedAt))
            .Include(t => t.Department)
            .FirstOrDefaultAsync(t => t.GuestToken == token);

        if (ticket == null) return NotFound(new { error = "Ticket not found." });

        return Ok(new
        {
            ticket.Id,
            ticket.Reference,
            ticket.Subject,
            ticket.Description,
            status = ticket.Status.ToValue(),
            priority = ticket.Priority.ToValue(),
            department = ticket.Department?.Name,
            ticket.CreatedAt,
            ticket.UpdatedAt,
            replies = ticket.Replies.Select(r => new
            {
                r.Id,
                r.Body,
                r.CreatedAt,
                authorType = r.AuthorType
            })
        });
    }

    /// <summary>
    /// Submit a reply on a guest ticket using the magic token.
    /// </summary>
    [HttpPost("tickets/{token}/reply")]
    // Throttled before the token lookup, so wrong-token guesses are counted too.
    [GuestThrottle(GuestThrottleScope.Reply)]
    public async Task<IActionResult> GuestReply(string token, [FromBody] GuestReplyRequest request)
    {
        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.GuestToken == token);
        if (ticket == null) return NotFound(new { error = "Ticket not found." });

        var reply = await _ticketService.AddReplyAsync(ticket, request.Body);
        return Ok(reply);
    }

    /// <summary>
    /// Submit a satisfaction rating for a ticket.
    /// </summary>
    [HttpPost("tickets/{token}/rate")]
    public async Task<IActionResult> Rate(string token, [FromBody] RateRequest request)
    {
        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.GuestToken == token);
        if (ticket == null) return NotFound();

        var existing = await _db.SatisfactionRatings.FirstOrDefaultAsync(r => r.TicketId == ticket.Id);
        if (existing != null)
            return BadRequest(new { error = "Rating already submitted." });

        var rating = new Models.SatisfactionRating
        {
            TicketId = ticket.Id,
            Rating = request.Rating,
            Comment = request.Comment,
            CreatedAt = DateTime.UtcNow
        };
        _db.SatisfactionRatings.Add(rating);
        await _db.SaveChangesAsync();

        return Ok(rating);
    }

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
public record GuestTicketRequest(string Subject, string? Description, string? Name, string? Email, int? DepartmentId = null, JsonElement? Metadata = null);
public record GuestReplyRequest(string Body);
public record RateRequest(int Rating, string? Comment = null);
public record FeedbackRequest(bool Helpful);
