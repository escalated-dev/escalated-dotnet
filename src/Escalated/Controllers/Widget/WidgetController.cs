using Escalated.Data;
using Escalated.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Controllers.Widget;

/// <summary>
/// Public API endpoints for the embeddable widget.
/// Supports KB search, anonymous ticket creation, and ticket status lookup by guest token.
/// </summary>
[ApiController]
[Route("support/widget")]
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
    public async Task<IActionResult> CreateGuestTicket([FromBody] GuestTicketRequest request)
    {
        var ticket = await _ticketService.CreateAsync(
            request.Subject,
            request.Description,
            guestName: request.Name,
            guestEmail: request.Email,
            departmentId: request.DepartmentId);

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

public record GuestTicketRequest(string Subject, string? Description, string? Name, string? Email, int? DepartmentId = null);
public record GuestReplyRequest(string Body);
public record RateRequest(int Rating, string? Comment = null);
public record FeedbackRequest(bool Helpful);
