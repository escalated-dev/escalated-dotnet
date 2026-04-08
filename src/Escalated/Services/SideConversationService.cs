using Escalated.Data;
using Escalated.Models;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Services;

public class SideConversationService
{
    private readonly EscalatedDbContext _db;

    public SideConversationService(EscalatedDbContext db)
    {
        _db = db;
    }

    public async Task<SideConversation> CreateAsync(int ticketId, string subject, int createdBy,
        CancellationToken ct = default)
    {
        var conversation = new SideConversation
        {
            TicketId = ticketId,
            Subject = subject,
            CreatedBy = createdBy,
            Status = "open",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.SideConversations.Add(conversation);
        await _db.SaveChangesAsync(ct);
        return conversation;
    }

    public async Task<SideConversationReply> AddReplyAsync(int conversationId, string body, int authorId,
        CancellationToken ct = default)
    {
        var reply = new SideConversationReply
        {
            SideConversationId = conversationId,
            Body = body,
            AuthorId = authorId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.SideConversationReplies.Add(reply);

        var conversation = await _db.SideConversations.FindAsync(new object[] { conversationId }, ct);
        if (conversation != null)
        {
            conversation.UpdatedAt = DateTime.UtcNow;
            _db.SideConversations.Update(conversation);
        }

        await _db.SaveChangesAsync(ct);
        return reply;
    }

    public async Task<SideConversation?> CloseAsync(int conversationId, CancellationToken ct = default)
    {
        var conversation = await _db.SideConversations.FindAsync(new object[] { conversationId }, ct);
        if (conversation == null) return null;

        conversation.Status = "closed";
        conversation.UpdatedAt = DateTime.UtcNow;
        _db.SideConversations.Update(conversation);
        await _db.SaveChangesAsync(ct);
        return conversation;
    }

    public async Task<List<SideConversation>> GetForTicketAsync(int ticketId,
        CancellationToken ct = default)
    {
        return await _db.SideConversations
            .Include(s => s.Replies)
            .Where(s => s.TicketId == ticketId)
            .OrderByDescending(s => s.UpdatedAt)
            .ToListAsync(ct);
    }
}
