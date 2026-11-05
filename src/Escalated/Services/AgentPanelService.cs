using System.Text.Json;
using Escalated.Authorization;
using Escalated.Configuration;
using Escalated.Data;
using Escalated.Hubs;
using Escalated.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Escalated.Services;

/// <summary>
/// The agent panel's data (escalated-developer-context ADR 2026-11-05-agent-panel-data,
/// after escalated-laravel #292-#296): each agent's own status, per-agent unread markers,
/// customer presence (shown on agent pages only) and the agents' in-app notifications.
/// The domain events reach it through <see cref="Events.AgentPanelEventDispatcher"/>.
/// </summary>
public class AgentPanelService
{
    /// <summary>The statuses an agent may set.</summary>
    public static readonly string[] Statuses = { "online", "away", "offline" };

    /// <summary>The SignalR group each agent's notifications go to.</summary>
    public static string NotificationGroup(string userId) => $"escalated.agents.{userId}";

    private static readonly TimeSpan PresenceThrottle = TimeSpan.FromSeconds(30);
    private const string Unavailable = "A ticket you can no longer view";

    private static readonly Dictionary<string, string> Titles = new()
    {
        ["ticket_assigned"] = "{0} was assigned to you",
        ["ticket_reply"] = "New reply on {0}",
        ["mention"] = "{1} mentioned you in {0}",
        ["sla_warning"] = "SLA due soon on {0}",
        ["sla_breached"] = "SLA breached on {0}",
        ["chat_transferred"] = "A chat was transferred to you ({0})",
        ["generic"] = "Update on {0}",
    };

    private readonly EscalatedDbContext _db;
    private readonly EscalatedOptions _options;
    private readonly IServiceProvider _services;

    public AgentPanelService(EscalatedDbContext db, IOptions<EscalatedOptions> options, IServiceProvider services)
    {
        _db = db;
        _options = options.Value;
        _services = services;
    }

    /// <summary>Now, in UTC, to the second: markers compare at that precision.</summary>
    public static DateTime Now()
    {
        var now = DateTime.UtcNow;
        return new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
    }

    private string Prefix => "/" + _options.RoutePrefix.Trim('/');

    // ---- Agent status ---------------------------------------------------------------

    /// <summary>The agent's status, <c>offline</c> until they set one.</summary>
    public async Task<string> StatusAsync(string? userId, CancellationToken ct = default)
    {
        if (userId is null) return "offline";
        var status = await _db.AgentProfiles.AsNoTracking().Where(p => p.UserId == userId)
            .Select(p => p.ChatStatus).FirstOrDefaultAsync(ct);
        return status is not null && Statuses.Contains(status) ? status : "offline";
    }

    /// <summary>Stores the agent's status on their profile, creating it when needed.</summary>
    public async Task SetStatusAsync(string userId, string status, CancellationToken ct = default)
    {
        if (!Statuses.Contains(status)) throw new ArgumentException($"Unknown status \"{status}\".", nameof(status));
        var profile = await _db.AgentProfiles.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (profile is null)
        {
            profile = new AgentProfile { UserId = userId };
            _db.AgentProfiles.Add(profile);
        }

        profile.ChatStatus = status;
        profile.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    // ---- Unread markers ---------------------------------------------------------------

    /// <summary>Marks the ticket read for the agent. The caller found it through the agent's scope.</summary>
    public Task MarkReadAsync(string? userId, int ticketId, CancellationToken ct = default) => MarkAsync(userId, ticketId, Now(), ct);

    /// <summary>Marks the ticket unread for the agent until they open it again.</summary>
    public Task MarkUnreadAsync(string? userId, int ticketId, CancellationToken ct = default) => MarkAsync(userId, ticketId, null, ct);

    private async Task MarkAsync(string? userId, int ticketId, DateTime? at, CancellationToken ct)
    {
        if (userId is null) return;
        var now = Now();
        var updated = await _db.TicketReads.Where(r => r.UserId == userId && r.TicketId == ticketId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.LastReadAt, at).SetProperty(r => r.UpdatedAt, now), ct);
        if (updated > 0) return;

        _db.TicketReads.Add(new TicketRead { TicketId = ticketId, UserId = userId, LastReadAt = at, CreatedAt = now, UpdatedAt = now });
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Another request added the row first.
            _db.ChangeTracker.Clear();
            await _db.TicketReads.Where(r => r.UserId == userId && r.TicketId == ticketId)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.LastReadAt, at).SetProperty(r => r.UpdatedAt, now), ct);
        }
    }

    /// <summary>
    /// <c>is_unread</c>, <c>requester_online</c> and <c>requester_last_seen_at</c> for each
    /// ticket, keyed by ticket id: one query for the tickets, one for the agent's markers and
    /// one for presence, whatever the page size. The tickets are read through the current
    /// visibility scope.
    /// </summary>
    public async Task<Dictionary<int, Dictionary<string, object?>>> RowFieldsAsync(string? userId, IReadOnlyCollection<int> ticketIds, CancellationToken ct = default)
    {
        var result = new Dictionary<int, Dictionary<string, object?>>();
        if (ticketIds.Count == 0) return result;

        var tickets = await _db.Tickets.AsNoTracking().Where(t => ticketIds.Contains(t.Id))
            .Select(t => new { t.Id, t.RequesterId, t.ContactId, t.LastCustomerActivityAt })
            .ToListAsync(ct);
        var reads = userId is null
            ? new Dictionary<int, DateTime?>()
            : await _db.TicketReads.AsNoTracking().Where(r => r.UserId == userId && ticketIds.Contains(r.TicketId))
                .ToDictionaryAsync(r => r.TicketId, r => r.LastReadAt, ct);

        var subjects = tickets.ToDictionary(t => t.Id, t => Subject(t.Id, t.RequesterId, t.ContactId));
        var subjectIds = subjects.Values.Select(s => s.Id).Distinct().ToList();
        var seen = (await _db.CustomerPresence.AsNoTracking().Where(p => subjectIds.Contains(p.SubjectId)).ToListAsync(ct))
            .ToDictionary(p => (p.SubjectType, p.SubjectId), p => p.LastSeenAt);

        var onlineAfter = DateTime.UtcNow.AddSeconds(-Math.Max(1, _options.Ui.PresenceOnlineWindowSeconds));
        foreach (var ticket in tickets)
        {
            var hasRead = reads.TryGetValue(ticket.Id, out var readAt);
            var lastSeen = seen.TryGetValue(subjects[ticket.Id], out var at) ? DateTime.SpecifyKind(at, DateTimeKind.Utc) : (DateTime?)null;
            result[ticket.Id] = new Dictionary<string, object?>
            {
                ["is_unread"] = Unread(hasRead, readAt, ticket.LastCustomerActivityAt),
                ["requester_online"] = lastSeen is not null && lastSeen >= onlineAfter,
                ["requester_last_seen_at"] = lastSeen,
            };
        }

        return result;
    }

    private static bool Unread(bool hasRead, DateTime? readAt, DateTime? activity)
    {
        if (hasRead && readAt is null) return true;
        if (activity is null) return false;
        return !hasRead || readAt < activity;
    }

    /// <summary>Whose presence a ticket shows: the requesting user, else the contact, else the ticket itself.</summary>
    public static (string Type, string Id) Subject(int ticketId, string? requesterId, int? contactId) =>
        requesterId is not null ? ("user", requesterId)
        : contactId is not null ? ("contact", contactId.Value.ToString())
        : ("ticket", ticketId.ToString());

    /// <summary>
    /// Whether a reply came from the customer side: a public reply with no author (a guest,
    /// an inbound email, a chat visitor) or by the requester. Agents' replies, internal notes
    /// and system replies are not.
    /// </summary>
    public static bool CustomerSide(Ticket ticket, Reply reply)
    {
        if (reply.IsInternalNote || reply.AuthorType is "system" or "agent") return false;
        return reply.AuthorId is null || reply.AuthorId == ticket.RequesterId;
    }

    /// <summary>Records customer activity on the ticket.</summary>
    public async Task CustomerActivityAsync(int ticketId, DateTime at, CancellationToken ct = default)
    {
        var when = DateTime.SpecifyKind(at.AddTicks(-(at.Ticks % TimeSpan.TicksPerSecond)), DateTimeKind.Utc);
        await TicketAccessScope.WithoutVisibilityAsync(async () =>
            await _db.Tickets.IgnoreQueryFilters().Where(t => t.Id == ticketId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.LastCustomerActivityAt, when), ct));
    }

    // ---- Customer presence ---------------------------------------------------------------

    /// <summary>Records that the signed-in customer is on the site, at most every 30 seconds.</summary>
    public Task SeeUserAsync(string? userId, CancellationToken ct = default) =>
        userId is null ? Task.CompletedTask : SeeAsync(("user", userId), ct);

    /// <summary>Records that a ticket's customer (requester, contact or guest) is on the site.</summary>
    public Task SeeTicketCustomerAsync(Ticket ticket, CancellationToken ct = default) =>
        SeeAsync(Subject(ticket.Id, ticket.RequesterId, ticket.ContactId), ct);

    /// <summary>
    /// A conditional UPDATE, so a customer seen within the throttle costs one statement; a
    /// new customer gets a row. Presence never fails the request: errors are swallowed.
    /// </summary>
    private async Task SeeAsync((string Type, string Id) subject, CancellationToken ct)
    {
        try
        {
            var now = Now();
            var before = now - PresenceThrottle;
            var touched = await _db.CustomerPresence
                .Where(p => p.SubjectType == subject.Type && p.SubjectId == subject.Id && p.LastSeenAt < before)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.LastSeenAt, now).SetProperty(p => p.UpdatedAt, now), ct);
            if (touched > 0 || await _db.CustomerPresence.AnyAsync(p => p.SubjectType == subject.Type && p.SubjectId == subject.Id, ct)) return;

            _db.CustomerPresence.Add(new CustomerPresence { SubjectType = subject.Type, SubjectId = subject.Id, LastSeenAt = now, CreatedAt = now, UpdatedAt = now });
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
        {
            _db.ChangeTracker.Clear();
        }
    }

    // ---- Notifications --------------------------------------------------------------------

    /// <summary>Whether the in-app notifications feed is on (<c>Escalated:Ui:AgentNotifications</c>).</summary>
    public bool NotificationsEnabled => _options.Ui.AgentNotifications;

    /// <summary>
    /// Stores an in-app notification for the agent about the ticket and sends it as
    /// <c>notification.created</c> to the agent's SignalR group. Nobody is notified of their
    /// own action (<paramref name="causerId"/>), and nothing is stored while the feed is off.
    /// </summary>
    public async Task NotifyAsync(string? userId, string type, Ticket ticket, IDictionary<string, object?>? extra, string? causerId, CancellationToken ct = default)
    {
        if (!NotificationsEnabled || string.IsNullOrEmpty(userId) || userId == causerId) return;

        var data = new Dictionary<string, object?> { ["ticket_id"] = ticket.Id, ["reference"] = ticket.Reference };
        if (extra is not null)
        {
            foreach (var (key, value) in extra) data[key] = value;
        }

        var notification = new AgentNotification
        {
            UserId = userId,
            Type = type,
            Data = JsonSerializer.Serialize(data),
            CreatedAt = Now(),
        };
        _db.AgentNotifications.Add(notification);
        await _db.SaveChangesAsync(ct);

        var hub = _services.GetService<IHubContext<EscalatedHub>>();
        if (hub is null) return;
        var access = _services.GetService<TicketAccess>();
        var visible = access is null || !access.IsRestricted || await access.AgentCanViewAsync(userId, ticket.Id, ct);
        var item = Item(notification, data, visible ? (ticket.Id, ticket.Reference, ticket.Subject) : null, hasTicket: true);
        await hub.Clients.Group(NotificationGroup(userId)).SendAsync("notification.created", item, ct);
    }

    /// <summary>The agent's unread notification count.</summary>
    public Task<int> UnreadCountAsync(string userId, CancellationToken ct = default) =>
        _db.AgentNotifications.CountAsync(n => n.UserId == userId && n.ReadAt == null, ct);

    /// <summary>A page of the agent's notifications, newest first: <c>{data, meta}</c>.</summary>
    public async Task<Dictionary<string, object?>> PageAsync(string userId, int page, int perPage, bool unreadOnly, CancellationToken ct = default)
    {
        var query = _db.AgentNotifications.AsNoTracking().Where(n => n.UserId == userId);
        if (unreadOnly) query = query.Where(n => n.ReadAt == null);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Skip((page - 1) * perPage).Take(perPage).ToListAsync(ct);

        var parsed = rows.ToDictionary(n => n.Id, n => Parse(n.Data));
        var ids = parsed.Values.Select(TicketId).Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
        var visible = ids.Count == 0
            ? new Dictionary<int, (int, string, string)>()
            : (await _db.Tickets.AsNoTracking().Where(t => ids.Contains(t.Id)).Select(t => new { t.Id, t.Reference, t.Subject }).ToListAsync(ct))
                .ToDictionary(t => t.Id, t => (t.Id, t.Reference, t.Subject));

        var items = rows.Select(n =>
        {
            var data = parsed[n.Id];
            var id = TicketId(data);
            (int, string, string)? ticket = id is not null && visible.TryGetValue(id.Value, out var found) ? found : null;
            return Item(n, data, ticket, hasTicket: id is not null);
        }).ToList();

        return new Dictionary<string, object?>
        {
            ["data"] = items,
            ["meta"] = new Dictionary<string, object?>
            {
                ["current_page"] = page,
                ["last_page"] = Math.Max(1, (total + perPage - 1) / perPage),
                ["per_page"] = perPage,
                ["total"] = total,
            },
        };
    }

    /// <summary>Marks one of the agent's notifications read; false when the agent has none with that id.</summary>
    public async Task<bool> ReadAsync(string userId, string id, CancellationToken ct = default)
    {
        if (!await _db.AgentNotifications.AnyAsync(n => n.UserId == userId && n.Id == id, ct)) return false;
        var now = Now();
        await _db.AgentNotifications.Where(n => n.UserId == userId && n.Id == id && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), ct);
        return true;
    }

    /// <summary>Marks every notification of the agent's read.</summary>
    public async Task ReadAllAsync(string userId, CancellationToken ct = default)
    {
        var now = Now();
        await _db.AgentNotifications.Where(n => n.UserId == userId && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), ct);
    }

    private Dictionary<string, object?> Item(AgentNotification n, Dictionary<string, object?> data, (int Id, string Reference, string Subject)? ticket, bool hasTicket)
    {
        var type = Titles.ContainsKey(n.Type) ? n.Type : "generic";
        var item = new Dictionary<string, object?> { ["id"] = n.Id, ["type"] = type };
        if (hasTicket && ticket is null)
        {
            item["title"] = Unavailable;
            item["body"] = null;
            item["ticket"] = null;
            item["url"] = null;
        }
        else
        {
            var reference = ticket?.Reference ?? data.GetValueOrDefault("reference")?.ToString() ?? string.Empty;
            item["title"] = string.Format(Titles[type], reference, data.GetValueOrDefault("mentioned_by")?.ToString() ?? string.Empty);
            item["body"] = ticket?.Subject;
            item["ticket"] = ticket is null ? null : new Dictionary<string, object?>
            {
                ["id"] = ticket.Value.Id,
                ["reference"] = ticket.Value.Reference,
                ["subject"] = ticket.Value.Subject,
            };
            item["url"] = ticket is null ? null : $"{Prefix}/agent/tickets/{Uri.EscapeDataString(ticket.Value.Reference)}";
        }

        item["created_at"] = DateTime.SpecifyKind(n.CreatedAt, DateTimeKind.Utc);
        item["read_at"] = n.ReadAt is null ? null : DateTime.SpecifyKind(n.ReadAt.Value, DateTimeKind.Utc);
        return item;
    }

    private static Dictionary<string, object?> Parse(string json)
    {
        var result = new Dictionary<string, object?>();
        try
        {
            using var document = JsonDocument.Parse(json);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                result[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.Number when property.Value.TryGetInt32(out var number) => number,
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Null => null,
                    _ => property.Value.ToString(),
                };
            }
        }
        catch (JsonException)
        {
            // A malformed row reads as one without a ticket.
        }

        return result;
    }

    private static int? TicketId(Dictionary<string, object?> data) => data.GetValueOrDefault("ticket_id") as int?;

    // ---- Knowledge base search -------------------------------------------------------------

    /// <summary>
    /// The agents' knowledge-base search: published articles whose title or body contains the
    /// term (case-insensitive; EF escapes <c>%</c> and <c>_</c> in a contains), title matches
    /// first, then the most recently updated.
    /// </summary>
    public Task<List<Article>> SearchArticlesAsync(string term, int limit, CancellationToken ct = default)
    {
        var needle = term.ToLowerInvariant();
        return _db.Articles.AsNoTracking().Include(a => a.Category)
            .Where(a => a.Status == "published")
            .Where(a => a.Title.ToLower().Contains(needle) || (a.Body != null && a.Body.ToLower().Contains(needle)))
            .OrderBy(a => a.Title.ToLower().Contains(needle) ? 0 : 1)
            .ThenByDescending(a => a.UpdatedAt)
            .ThenByDescending(a => a.Id)
            .Take(limit)
            .ToListAsync(ct);
    }
}
