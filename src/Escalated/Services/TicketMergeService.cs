using Escalated.Data;
using Escalated.Enums;
using Escalated.Models;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Services;

public class TicketMergeService
{
    private readonly EscalatedDbContext _db;

    public TicketMergeService(EscalatedDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Merge source ticket into target ticket. Moves replies, adds system notes, and closes source.
    /// </summary>
    public async Task MergeAsync(Ticket source, Ticket target, int? mergedByUserId = null,
        CancellationToken ct = default)
    {
        using var transaction = await _db.Database.BeginTransactionAsync(ct);

        try
        {
            // Move all replies from source to target
            var replies = await _db.Replies.Where(r => r.TicketId == source.Id).ToListAsync(ct);
            foreach (var reply in replies)
            {
                reply.TicketId = target.Id;
            }

            // System note on target
            _db.Replies.Add(new Reply
            {
                TicketId = target.Id,
                Body = $"Ticket {source.Reference} was merged into this ticket.",
                IsInternalNote = true,
                IsPinned = false,
                Type = "note",
                Metadata = System.Text.Json.JsonSerializer.Serialize(new
                {
                    system_note = true,
                    merge_source = source.Reference,
                    merged_by = mergedByUserId
                }),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            // System note on source
            _db.Replies.Add(new Reply
            {
                TicketId = source.Id,
                Body = $"This ticket was merged into {target.Reference}.",
                IsInternalNote = true,
                IsPinned = false,
                Type = "note",
                Metadata = System.Text.Json.JsonSerializer.Serialize(new
                {
                    system_note = true,
                    merge_target = target.Reference,
                    merged_by = mergedByUserId
                }),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            // Close source and set merged_into
            source.Status = TicketStatus.Closed;
            source.MergedIntoId = target.Id;
            source.ClosedAt = DateTime.UtcNow;
            source.UpdatedAt = DateTime.UtcNow;
            _db.Tickets.Update(source);

            // Log activity
            _db.TicketActivities.Add(new TicketActivity
            {
                TicketId = source.Id,
                Type = ActivityType.TicketMerged,
                CauserId = mergedByUserId,
                Properties = System.Text.Json.JsonSerializer.Serialize(new
                {
                    merged_into = target.Reference,
                    merged_into_id = target.Id
                }),
                CreatedAt = DateTime.UtcNow
            });

            _db.TicketActivities.Add(new TicketActivity
            {
                TicketId = target.Id,
                Type = ActivityType.TicketMerged,
                CauserId = mergedByUserId,
                Properties = System.Text.Json.JsonSerializer.Serialize(new
                {
                    merged_from = source.Reference,
                    merged_from_id = source.Id
                }),
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }
}
