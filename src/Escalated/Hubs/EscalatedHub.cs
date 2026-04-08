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

    // ── Live Chat ──

    /// <summary>
    /// Join the chat queue feed to receive notifications of new chat sessions.
    /// </summary>
    public async Task JoinChatQueue()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "chat-queue");
    }

    /// <summary>
    /// Leave the chat queue feed.
    /// </summary>
    public async Task LeaveChatQueue()
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "chat-queue");
    }

    /// <summary>
    /// Join a specific chat session room to receive live messages.
    /// </summary>
    public async Task JoinChat(int sessionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"chat-{sessionId}");
    }

    /// <summary>
    /// Leave a chat session room.
    /// </summary>
    public async Task LeaveChat(int sessionId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"chat-{sessionId}");
    }

    /// <summary>
    /// Broadcast a typing indicator within a chat session.
    /// </summary>
    public async Task ChatTyping(int sessionId, string userName)
    {
        await Clients.OthersInGroup($"chat-{sessionId}")
            .SendAsync("ChatTyping", new { sessionId, userName });
    }
}
