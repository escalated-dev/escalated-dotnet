using Escalated.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Escalated.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace Escalated.Controllers.Agent;

/// <summary>
/// The authenticated agent's @-mention inbox. Mirrors the Laravel reference
/// <c>Mention</c> model's <c>forUser</c> / <c>unread</c> scopes and
/// <c>markAsRead</c>. The agent is the user the host signed in; a <c>userId</c>
/// in the query string is ignored.
/// </summary>
[ApiController]
[Route("support/agent/mentions")]
[Authorize(Policy = EscalatedPolicies.Agent)]
public class AgentMentionController : ControllerBase
{
    private readonly EscalatedDbContext _db;

    public AgentMentionController(EscalatedDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] bool unreadOnly = false)
    {
        if (this.CurrentUserId() is not { } userId) return Unauthorized();

        var query = _db.Mentions.Where(m => m.UserId == userId);
        if (unreadOnly) query = query.Where(m => m.ReadAt == null);

        var mentions = await query
            .OrderByDescending(m => m.CreatedAt)
            .Include(m => m.Reply)!
                .ThenInclude(r => r!.Ticket)
            .ToListAsync();

        var data = mentions.Select(m => new
        {
            id = m.Id,
            reply_id = m.ReplyId,
            ticket_id = m.Reply?.TicketId,
            ticket_reference = m.Reply?.Ticket?.Reference,
            ticket_subject = m.Reply?.Ticket?.Subject,
            mentioned_by = m.Reply?.AuthorId,
            read_at = m.ReadAt,
            created_at = m.CreatedAt,
        });

        return Ok(new { data });
    }

    [HttpPost("{id:int}/read")]
    public async Task<IActionResult> MarkRead(int id)
    {
        if (this.CurrentUserId() is not { } userId) return Unauthorized();

        var mention = await _db.Mentions.FirstOrDefaultAsync(m => m.Id == id && m.UserId == userId);
        if (mention == null) return NotFound();

        mention.ReadAt ??= DateTime.UtcNow;
        mention.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new { id = mention.Id, read_at = mention.ReadAt });
    }
}
