using System.Collections.Concurrent;
using Escalated.Authorization;
using Escalated.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Hubs;

public record TicketSubscription(string ConnectionId, string Room, string? UserId, string? GuestToken, int? GuestTicketId, bool StaffOnly);

/// <summary>Current local SignalR subscriptions. Register a distributed equivalent when routing across processes.</summary>
public class TicketRealtimeAudience
{
    private readonly ConcurrentDictionary<(string Connection, string Room), TicketSubscription> subscriptions = new();
    public virtual IEnumerable<TicketSubscription> Members => subscriptions.Values;
    public virtual void Add(TicketSubscription member) => subscriptions[(member.ConnectionId, member.Room)] = member;
    public virtual void Remove(string connection, string room) => subscriptions.TryRemove((connection, room), out _);
    public virtual void RemoveConnection(string connection)
    {
        foreach (var key in subscriptions.Keys.Where(key => key.Connection == connection)) subscriptions.TryRemove(key, out _);
    }
}

/// <summary>Host event publishers use this service to recheck each recipient before transmitting ticket payloads.</summary>
public sealed class TicketBroadcastService
{
    private readonly TicketAccess access;
    private readonly EscalatedDbContext db;
    private readonly TicketRealtimeAudience audience;
    private readonly IHubContext<EscalatedHub>? hub;
    private readonly bool legacyGuestTokens;

    public TicketBroadcastService(TicketAccess access, EscalatedDbContext db, TicketRealtimeAudience audience, IHubContext<EscalatedHub>? hub = null,
        Microsoft.Extensions.Options.IOptions<Escalated.Configuration.EscalatedOptions>? options = null)
    {
        this.access = access;
        this.db = db;
        this.audience = audience;
        this.hub = hub;
        legacyGuestTokens = options?.Value.GuestAccess.LegacyTokens == true;
    }

    public async Task<IReadOnlyList<string>> RecipientsAsync(int ticketId, string room, CancellationToken ct = default)
    {
        var result = new List<string>();
        foreach (var member in audience.Members.Where(member => member.Room == room))
        {
            if (await CanReceiveAsync(member, ticketId, ct)) result.Add(member.ConnectionId);
        }
        return result;
    }

    private async Task<bool> CanReceiveAsync(TicketSubscription member, int ticketId, CancellationToken ct)
    {
        if (member.UserId is not null)
        {
            var principal = await access.PrincipalAsync(member.UserId);
            return (!member.StaffOnly || await access.IsAdminAsync(principal) || await access.IsAgentAsync(principal))
                && await access.CanViewAsync(principal, ticketId, ct);
        }
        return member.GuestTicketId == ticketId && member.GuestToken is not null
            && await TicketAccessScope.WithoutVisibilityAsync(() => db.Tickets.AsNoTracking()
                .Where(Escalated.Services.GuestAccessService.Opens(member.GuestToken, legacyGuestTokens, DateTime.UtcNow))
                .AnyAsync(ticket => ticket.Id == ticketId, ct));
    }

    public async Task SendAsync(int ticketId, string room, string eventName, object payload, string? exceptConnection = null, CancellationToken ct = default)
    {
        if (hub is null) return;
        if (!access.IsRestricted)
        {
            await hub.Clients.GroupExcept(room, exceptConnection is null ? Array.Empty<string>() : new[] { exceptConnection }).SendAsync(eventName, payload, ct);
            return;
        }
        foreach (var member in audience.Members.Where(member => member.Room == room && member.ConnectionId != exceptConnection))
            if (await CanReceiveAsync(member, ticketId, ct)) await hub.Clients.Client(member.ConnectionId).SendAsync(eventName, payload, ct);
    }
}
