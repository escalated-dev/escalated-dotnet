using Escalated.Configuration;
using Escalated.Data;
using Escalated.Enums;
using Escalated.Events;
using Escalated.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Escalated.Services;

public class TicketService
{
    private readonly EscalatedDbContext _db;
    private readonly IEscalatedEventDispatcher _events;
    private readonly EscalatedOptions _options;

    public TicketService(EscalatedDbContext db, IEscalatedEventDispatcher events, IOptions<EscalatedOptions> options)
    {
        _db = db;
        _events = events;
        _options = options.Value;
    }

    public async Task<Ticket> CreateAsync(string subject, string? description, int? requesterId = null,
        string? requesterType = null, TicketPriority? priority = null, int? departmentId = null,
        string? guestName = null, string? guestEmail = null, string? ticketType = null,
        CancellationToken ct = default)
    {
        var ticket = new Ticket
        {
            Subject = subject,
            Description = description,
            Status = TicketStatus.Open,
            Priority = priority ?? TicketPriorityExtensions.Parse(_options.DefaultPriority),
            RequesterId = requesterId,
            RequesterType = requesterType,
            DepartmentId = departmentId,
            GuestName = guestName,
            GuestEmail = guestEmail,
            TicketType = ticketType,
            Reference = $"TEMP-{Guid.NewGuid()}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        if (ticket.IsGuest || (requesterId == null && !string.IsNullOrEmpty(guestEmail)))
        {
            ticket.GuestToken = Guid.NewGuid().ToString("N");
        }

        // Dedupe repeat guests by email (Pattern B). Inline Guest* fields
        // remain populated for the backwards-compat dual-read period. Any
        // store error here is non-fatal — ticket creation must never
        // block on the Contact lookup.
        if (requesterId == null && !string.IsNullOrEmpty(guestEmail))
        {
            var contact = await FindOrCreateContactAsync(guestEmail, guestName, ct);
            if (contact != null)
            {
                ticket.ContactId = contact.Id;
            }
        }

        _db.Tickets.Add(ticket);
        await _db.SaveChangesAsync(ct);

        // Generate final reference from ID
        ticket.Reference = ticket.GenerateReference(_options.TicketReferencePrefix);
        _db.Tickets.Update(ticket);
        await _db.SaveChangesAsync(ct);

        await _events.DispatchAsync(new TicketCreatedEvent(ticket), ct);

        return ticket;
    }

    public async Task<Ticket> UpdateAsync(Ticket ticket, string? subject = null, string? description = null,
        string? ticketType = null, CancellationToken ct = default)
    {
        if (subject != null) ticket.Subject = subject;
        if (description != null) ticket.Description = description;
        if (ticketType != null) ticket.TicketType = ticketType;
        ticket.UpdatedAt = DateTime.UtcNow;

        _db.Tickets.Update(ticket);
        await _db.SaveChangesAsync(ct);

        await _events.DispatchAsync(new TicketUpdatedEvent(ticket), ct);
        return ticket;
    }

    public async Task<Ticket> ChangeStatusAsync(Ticket ticket, TicketStatus newStatus, int? causerId = null,
        CancellationToken ct = default)
    {
        return newStatus switch
        {
            TicketStatus.Resolved => await MarkResolvedAsync(ticket, causerId, ct),
            TicketStatus.Closed => await MarkClosedAsync(ticket, causerId, ct),
            TicketStatus.Reopened => await MarkReopenedAsync(ticket, causerId, ct),
            TicketStatus.Escalated => await MarkEscalatedAsync(ticket, causerId, ct),
            _ => await TransitionToAsync(ticket, newStatus, causerId, ct)
        };
    }

    public async Task<Ticket> ChangePriorityAsync(Ticket ticket, TicketPriority newPriority, int? causerId = null,
        CancellationToken ct = default)
    {
        var oldPriority = ticket.Priority;
        ticket.Priority = newPriority;
        ticket.UpdatedAt = DateTime.UtcNow;
        _db.Tickets.Update(ticket);

        await LogActivityAsync(ticket, ActivityType.PriorityChanged, causerId, new Dictionary<string, object>
        {
            ["old_priority"] = oldPriority.ToValue(),
            ["new_priority"] = newPriority.ToValue()
        }, ct);

        await _db.SaveChangesAsync(ct);
        await _events.DispatchAsync(new TicketPriorityChangedEvent(ticket, oldPriority, newPriority, causerId), ct);
        return ticket;
    }

    public async Task<Ticket> ChangeDepartmentAsync(Ticket ticket, int departmentId, int? causerId = null,
        CancellationToken ct = default)
    {
        var oldDeptId = ticket.DepartmentId;
        ticket.DepartmentId = departmentId;
        ticket.UpdatedAt = DateTime.UtcNow;
        _db.Tickets.Update(ticket);

        await LogActivityAsync(ticket, ActivityType.DepartmentChanged, causerId, new Dictionary<string, object>
        {
            ["old_department_id"] = oldDeptId ?? 0,
            ["new_department_id"] = departmentId
        }, ct);

        await _db.SaveChangesAsync(ct);
        await _events.DispatchAsync(new DepartmentChangedEvent(ticket, oldDeptId, departmentId, causerId), ct);
        return ticket;
    }

    public async Task<Reply> AddReplyAsync(Ticket ticket, string body, int? authorId = null,
        string? authorType = null, bool isNote = false, CancellationToken ct = default)
    {
        var reply = new Reply
        {
            TicketId = ticket.Id,
            Body = body,
            AuthorId = authorId,
            AuthorType = authorType,
            IsInternalNote = isNote,
            Type = isNote ? "note" : "reply",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Replies.Add(reply);

        var activityType = isNote ? ActivityType.NoteAdded : ActivityType.Replied;
        await LogActivityAsync(ticket, activityType, authorId, null, ct);

        ticket.UpdatedAt = DateTime.UtcNow;
        _db.Tickets.Update(ticket);
        await _db.SaveChangesAsync(ct);

        if (isNote)
            await _events.DispatchAsync(new InternalNoteAddedEvent(reply), ct);
        else
            await _events.DispatchAsync(new ReplyCreatedEvent(reply), ct);

        return reply;
    }

    public async Task<Ticket> AddTagsAsync(Ticket ticket, int[] tagIds, int? causerId = null,
        CancellationToken ct = default)
    {
        foreach (var tagId in tagIds)
        {
            var exists = await _db.TicketTags.AnyAsync(tt => tt.TicketId == ticket.Id && tt.TagId == tagId, ct);
            if (!exists)
            {
                _db.TicketTags.Add(new TicketTag { TicketId = ticket.Id, TagId = tagId });
                await LogActivityAsync(ticket, ActivityType.TagAdded, causerId, new Dictionary<string, object>
                {
                    ["tag_id"] = tagId
                }, ct);
                await _events.DispatchAsync(new TagAddedEvent(ticket, tagId, causerId), ct);
            }
        }
        await _db.SaveChangesAsync(ct);
        return ticket;
    }

    public async Task<Ticket> RemoveTagsAsync(Ticket ticket, int[] tagIds, int? causerId = null,
        CancellationToken ct = default)
    {
        var tagsToRemove = await _db.TicketTags
            .Where(tt => tt.TicketId == ticket.Id && tagIds.Contains(tt.TagId))
            .ToListAsync(ct);

        foreach (var tt in tagsToRemove)
        {
            _db.TicketTags.Remove(tt);
            await LogActivityAsync(ticket, ActivityType.TagRemoved, causerId, new Dictionary<string, object>
            {
                ["tag_id"] = tt.TagId
            }, ct);
            await _events.DispatchAsync(new TagRemovedEvent(ticket, tt.TagId, causerId), ct);
        }
        await _db.SaveChangesAsync(ct);
        return ticket;
    }

    public async Task<Ticket?> FindByIdAsync(int id, CancellationToken ct = default)
    {
        return await _db.Tickets.FindAsync(new object[] { id }, ct);
    }

    public async Task<Ticket?> FindByReferenceAsync(string reference, CancellationToken ct = default)
    {
        return await _db.Tickets.FirstOrDefaultAsync(t => t.Reference == reference, ct);
    }

    public IQueryable<Ticket> Query() => _db.Tickets.AsQueryable();

    public async Task<(List<Ticket> Items, int TotalCount)> ListAsync(
        TicketListFilters? filters = null, int page = 1, int perPage = 25,
        CancellationToken ct = default)
    {
        var query = _db.Tickets.AsQueryable();
        filters ??= new TicketListFilters();

        if (filters.Status.HasValue)
            query = query.Where(t => t.Status == filters.Status.Value);

        if (filters.Priority.HasValue)
            query = query.Where(t => t.Priority == filters.Priority.Value);

        if (filters.AssignedTo.HasValue)
            query = query.Where(t => t.AssignedTo == filters.AssignedTo.Value);

        if (filters.Unassigned == true)
            query = query.Where(t => t.AssignedTo == null);

        if (filters.DepartmentId.HasValue)
            query = query.Where(t => t.DepartmentId == filters.DepartmentId.Value);

        if (!string.IsNullOrEmpty(filters.Search))
        {
            var term = filters.Search;
            query = query.Where(t =>
                t.Subject.Contains(term) ||
                t.Reference.Contains(term) ||
                (t.Description != null && t.Description.Contains(term)) ||
                (t.GuestName != null && t.GuestName.Contains(term)) ||
                (t.GuestEmail != null && t.GuestEmail.Contains(term)));
        }

        if (filters.SlaBreach == true)
            query = query.Where(t => t.SlaFirstResponseBreached || t.SlaResolutionBreached);

        if (!string.IsNullOrEmpty(filters.TicketType))
            query = query.Where(t => t.TicketType == filters.TicketType);

        if (filters.RequesterId.HasValue)
            query = query.Where(t => t.RequesterId == filters.RequesterId.Value);

        // Sort
        query = (filters.SortBy?.ToLower(), filters.SortDir?.ToLower()) switch
        {
            ("priority", "asc") => query.OrderBy(t => t.Priority),
            ("priority", _) => query.OrderByDescending(t => t.Priority),
            ("status", "asc") => query.OrderBy(t => t.Status),
            ("status", _) => query.OrderByDescending(t => t.Status),
            ("updated_at", "asc") => query.OrderBy(t => t.UpdatedAt),
            (_, "asc") => query.OrderBy(t => t.CreatedAt),
            _ => query.OrderByDescending(t => t.CreatedAt)
        };

        var totalCount = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * perPage).Take(perPage).ToListAsync(ct);

        return (items, totalCount);
    }

    // Private transition methods

    private async Task<Ticket> MarkResolvedAsync(Ticket ticket, int? causerId, CancellationToken ct)
    {
        var oldStatus = ticket.Status;
        if (!oldStatus.CanTransitionTo(TicketStatus.Resolved))
            throw new InvalidOperationException($"Cannot transition from {oldStatus} to Resolved.");

        ticket.Status = TicketStatus.Resolved;
        ticket.ResolvedAt = DateTime.UtcNow;
        ticket.UpdatedAt = DateTime.UtcNow;
        _db.Tickets.Update(ticket);

        await LogActivityAsync(ticket, ActivityType.Resolved, causerId, new Dictionary<string, object>
        {
            ["old_status"] = oldStatus.ToValue(),
            ["new_status"] = "resolved"
        }, ct);

        await _db.SaveChangesAsync(ct);
        await _events.DispatchAsync(new TicketStatusChangedEvent(ticket, oldStatus, TicketStatus.Resolved, causerId), ct);
        await _events.DispatchAsync(new TicketResolvedEvent(ticket, causerId), ct);
        return ticket;
    }

    private async Task<Ticket> MarkClosedAsync(Ticket ticket, int? causerId, CancellationToken ct)
    {
        var oldStatus = ticket.Status;
        if (!oldStatus.CanTransitionTo(TicketStatus.Closed))
            throw new InvalidOperationException($"Cannot transition from {oldStatus} to Closed.");

        ticket.Status = TicketStatus.Closed;
        ticket.ClosedAt = DateTime.UtcNow;
        ticket.UpdatedAt = DateTime.UtcNow;
        _db.Tickets.Update(ticket);

        await LogActivityAsync(ticket, ActivityType.Closed, causerId, new Dictionary<string, object>
        {
            ["old_status"] = oldStatus.ToValue(),
            ["new_status"] = "closed"
        }, ct);

        await _db.SaveChangesAsync(ct);
        await _events.DispatchAsync(new TicketStatusChangedEvent(ticket, oldStatus, TicketStatus.Closed, causerId), ct);
        await _events.DispatchAsync(new TicketClosedEvent(ticket, causerId), ct);
        return ticket;
    }

    private async Task<Ticket> MarkReopenedAsync(Ticket ticket, int? causerId, CancellationToken ct)
    {
        var oldStatus = ticket.Status;
        if (!oldStatus.CanTransitionTo(TicketStatus.Reopened))
            throw new InvalidOperationException($"Cannot transition from {oldStatus} to Reopened.");

        ticket.Status = TicketStatus.Reopened;
        ticket.ResolvedAt = null;
        ticket.ClosedAt = null;
        ticket.UpdatedAt = DateTime.UtcNow;
        _db.Tickets.Update(ticket);

        await LogActivityAsync(ticket, ActivityType.Reopened, causerId, new Dictionary<string, object>
        {
            ["old_status"] = oldStatus.ToValue(),
            ["new_status"] = "reopened"
        }, ct);

        await _db.SaveChangesAsync(ct);
        await _events.DispatchAsync(new TicketStatusChangedEvent(ticket, oldStatus, TicketStatus.Reopened, causerId), ct);
        await _events.DispatchAsync(new TicketReopenedEvent(ticket, causerId), ct);
        return ticket;
    }

    private async Task<Ticket> MarkEscalatedAsync(Ticket ticket, int? causerId, CancellationToken ct)
    {
        var oldStatus = ticket.Status;
        if (!oldStatus.CanTransitionTo(TicketStatus.Escalated))
            throw new InvalidOperationException($"Cannot transition from {oldStatus} to Escalated.");

        ticket.Status = TicketStatus.Escalated;
        ticket.UpdatedAt = DateTime.UtcNow;
        _db.Tickets.Update(ticket);

        await LogActivityAsync(ticket, ActivityType.Escalated, causerId, new Dictionary<string, object>
        {
            ["old_status"] = oldStatus.ToValue(),
            ["new_status"] = "escalated"
        }, ct);

        await _db.SaveChangesAsync(ct);
        await _events.DispatchAsync(new TicketStatusChangedEvent(ticket, oldStatus, TicketStatus.Escalated, causerId), ct);
        await _events.DispatchAsync(new TicketEscalatedEvent(ticket, causerId), ct);
        return ticket;
    }

    private async Task<Ticket> TransitionToAsync(Ticket ticket, TicketStatus newStatus, int? causerId, CancellationToken ct)
    {
        var oldStatus = ticket.Status;
        if (!oldStatus.CanTransitionTo(newStatus))
            throw new InvalidOperationException($"Cannot transition from {oldStatus} to {newStatus}.");

        ticket.Status = newStatus;
        ticket.UpdatedAt = DateTime.UtcNow;
        _db.Tickets.Update(ticket);

        await LogActivityAsync(ticket, ActivityType.StatusChanged, causerId, new Dictionary<string, object>
        {
            ["old_status"] = oldStatus.ToValue(),
            ["new_status"] = newStatus.ToValue()
        }, ct);

        await _db.SaveChangesAsync(ct);
        await _events.DispatchAsync(new TicketStatusChangedEvent(ticket, oldStatus, newStatus, causerId), ct);
        return ticket;
    }

    public async Task LogActivityAsync(Ticket ticket, ActivityType type, int? causerId,
        Dictionary<string, object>? properties = null, CancellationToken ct = default)
    {
        var activity = new TicketActivity
        {
            TicketId = ticket.Id,
            Type = type,
            CauserId = causerId,
            Properties = properties != null
                ? System.Text.Json.JsonSerializer.Serialize(properties)
                : null,
            CreatedAt = DateTime.UtcNow
        };
        _db.TicketActivities.Add(activity);
    }

    /// <summary>
    /// Resolve a Contact by email (normalized trim + lowercase) or
    /// create one. Mirrors the Pattern B reference impl — uses
    /// <see cref="Contact.NormalizeEmail"/> + <see cref="Contact.DecideAction"/>
    /// for branch selection.
    /// </summary>
    private async Task<Contact?> FindOrCreateContactAsync(
        string email, string? name, CancellationToken ct)
    {
        var normalized = Contact.NormalizeEmail(email);
        if (string.IsNullOrEmpty(normalized)) return null;

        var existing = await _db.Contacts
            .FirstOrDefaultAsync(c => c.Email == normalized, ct);

        var action = Contact.DecideAction(existing, name);
        switch (action)
        {
            case "return-existing":
                return existing;

            case "update-name":
                existing!.Name = name;
                existing.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
                return existing;

            case "create":
            default:
                var created = new Contact
                {
                    Email = normalized,
                    Name = name,
                    Metadata = "{}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                };
                _db.Contacts.Add(created);
                await _db.SaveChangesAsync(ct);
                return created;
        }
    }
}

public class TicketListFilters
{
    public TicketStatus? Status { get; set; }
    public TicketPriority? Priority { get; set; }
    public int? AssignedTo { get; set; }
    public bool? Unassigned { get; set; }
    public int? DepartmentId { get; set; }
    public string? Search { get; set; }
    public bool? SlaBreach { get; set; }
    public string? TicketType { get; set; }
    public int? RequesterId { get; set; }
    public string? SortBy { get; set; }
    public string? SortDir { get; set; }
}
