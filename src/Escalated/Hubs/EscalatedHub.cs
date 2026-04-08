using Microsoft.AspNetCore.SignalR;

namespace Escalated.Hubs;

/// <summary>
/// SignalR hub for real-time ticket updates (opt-in via EscalatedOptions.EnableRealTime).
/// Clients join/leave ticket-specific groups to receive live updates.
/// </summary>
public class EscalatedHub : Hub
{
    /// <summary>
    /// Join a ticket room to receive live updates for that ticket.
    /// </summary>
    public async Task JoinTicket(int ticketId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"ticket-{ticketId}");
    }

    /// <summary>
    /// Leave a ticket room.
    /// </summary>
    public async Task LeaveTicket(int ticketId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"ticket-{ticketId}");
    }

    /// <summary>
    /// Join the global ticket list feed.
    /// </summary>
    public async Task JoinTicketList()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "ticket-list");
    }

    /// <summary>
    /// Leave the global ticket list feed.
    /// </summary>
    public async Task LeaveTicketList()
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "ticket-list");
    }

    /// <summary>
    /// Broadcast a presence ping for a ticket (agent is viewing it).
    /// </summary>
    public async Task Presence(int ticketId, string userName)
    {
        await Clients.OthersInGroup($"ticket-{ticketId}")
            .SendAsync("PresenceUpdate", new { ticketId, userName, connectionId = Context.ConnectionId });
    }
}
