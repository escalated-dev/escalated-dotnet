using Escalated.Data;
using Escalated.Enums;
using Escalated.Events;
using Escalated.Models;
using Microsoft.Extensions.Options;
using Escalated.Configuration;

namespace Escalated.Services;

public class TicketSplitService
{
    private readonly EscalatedDbContext _db;
    private readonly IEscalatedEventDispatcher _events;
    private readonly EscalatedOptions _options;

    public TicketSplitService(EscalatedDbContext db, IEscalatedEventDispatcher events, IOptions<EscalatedOptions> options)
    {
        _db = db;
        _events = events;
        _options = options.Value;
    }

    /// <summary>
    /// Split a reply from the source ticket into a new ticket.
    /// </summary>
    public async Task<Ticket> SplitAsync(Ticket source, Reply reply, string? subject = null,
        CancellationToken ct = default)
    {
        using var transaction = await _db.Database.BeginTransactionAsync(ct);

        try
        {
            var newSubject = subject ?? $"Split from {source.Reference}: {source.Subject}";

            var newTicket = new Ticket
            {
                Subject = newSubject,
                Description = reply.Body,
                Status = TicketStatus.Open,
                Priority = source.Priority,
                RequesterType = source.RequesterType,
                RequesterId = source.RequesterId,
                DepartmentId = source.DepartmentId,
                GuestName = source.GuestName,
                GuestEmail = source.GuestEmail,
                Reference = $"TEMP-{Guid.NewGuid()}",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _db.Tickets.Add(newTicket);
            await _db.SaveChangesAsync(ct);

            // Generate final reference
            newTicket.Reference = newTicket.GenerateReference(_options.TicketReferencePrefix);
            _db.Tickets.Update(newTicket);

            // Copy tags
            var tagIds = _db.TicketTags
                .Where(tt => tt.TicketId == source.Id)
                .Select(tt => tt.TagId)
                .ToList();

            foreach (var tagId in tagIds)
            {
                _db.TicketTags.Add(new TicketTag { TicketId = newTicket.Id, TagId = tagId });
            }

            // Create ticket link
            _db.TicketLinks.Add(new TicketLink
            {
                ParentTicketId = source.Id,
                ChildTicketId = newTicket.Id,
                LinkType = "split",
                CreatedAt = DateTime.UtcNow
            });

            // System note on source
            _db.Replies.Add(new Reply
            {
                TicketId = source.Id,
                Body = $"Split to #{newTicket.Reference}",
                IsInternalNote = true,
                Type = "note",
                Metadata = System.Text.Json.JsonSerializer.Serialize(new
                {
                    system_note = true,
                    split_to = newTicket.Reference
                }),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            // Activity on source
            _db.TicketActivities.Add(new TicketActivity
            {
                TicketId = source.Id,
                Type = ActivityType.TicketSplit,
                Properties = System.Text.Json.JsonSerializer.Serialize(new
                {
                    split_to = newTicket.Reference,
                    split_to_id = newTicket.Id
                }),
                CreatedAt = DateTime.UtcNow
            });

            // System note on new ticket
            _db.Replies.Add(new Reply
            {
                TicketId = newTicket.Id,
                Body = $"Split from #{source.Reference}",
                IsInternalNote = true,
                Type = "note",
                Metadata = System.Text.Json.JsonSerializer.Serialize(new
                {
                    system_note = true,
                    split_from = source.Reference
                }),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            // Activity on new ticket
            _db.TicketActivities.Add(new TicketActivity
            {
                TicketId = newTicket.Id,
                Type = ActivityType.TicketSplit,
                Properties = System.Text.Json.JsonSerializer.Serialize(new
                {
                    split_from = source.Reference,
                    split_from_id = source.Id
                }),
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            await _events.DispatchAsync(new TicketCreatedEvent(newTicket), ct);

            return newTicket;
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }
}
