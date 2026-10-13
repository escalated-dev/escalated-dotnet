using Microsoft.AspNetCore.SignalR;
using Escalated.Authorization;
using Escalated.Data;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Hubs;

/// <summary>
/// SignalR hub for real-time ticket updates (opt-in via EscalatedOptions.EnableRealTime).
/// Clients join/leave ticket-specific groups to receive live updates.
/// </summary>
public class EscalatedHub : Hub
{
    private readonly TicketAccess? access;
    private readonly EscalatedDbContext? db;
    private readonly TicketRealtimeAudience? audience;
    private readonly TicketBroadcastService? broadcasts;
    public EscalatedHub(TicketAccess? access = null, EscalatedDbContext? db = null, TicketRealtimeAudience? audience = null, TicketBroadcastService? broadcasts = null)
    {
        this.access = access;
        this.db = db;
        this.audience = audience;
        this.broadcasts = broadcasts;
    }

    private bool Restricted => access?.IsRestricted == true;

    private async Task SubscribeAsync(string room, int? ticketId = null, bool staffOnly = false)
    {
        var userId = access!.UserId(Context.User);
        string? token = null;
        if (userId is not null)
        {
            if (staffOnly && !await access.IsAdminAsync(Context.User!) && !await access.IsAgentAsync(Context.User!)) throw new HubException("Access denied.");
            if (ticketId is not null && !await access.CanViewAsync(Context.User!, ticketId.Value)) throw new HubException("Ticket not found.");
        }
        else
        {
            token = Context.GetHttpContext()?.Request.Query["guest_token"].ToString();
            if (staffOnly || ticketId is null || string.IsNullOrEmpty(token)
                || !await TicketAccessScope.WithoutVisibilityAsync(() => db!.Tickets.AnyAsync(ticket => ticket.Id == ticketId && ticket.GuestToken == token)))
                throw new HubException("Access denied.");
        }
        audience!.Add(new TicketSubscription(Context.ConnectionId, room, userId, token, userId is null ? ticketId : null, staffOnly));
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        audience?.RemoveConnection(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Join a ticket room to receive live updates for that ticket.
    /// </summary>
    public async Task JoinTicket(int ticketId)
    {
        if (Restricted) { await SubscribeAsync($"ticket-{ticketId}", ticketId); return; }
        await Groups.AddToGroupAsync(Context.ConnectionId, $"ticket-{ticketId}");
    }

    /// <summary>
    /// Leave a ticket room.
    /// </summary>
    public async Task LeaveTicket(int ticketId)
    {
        audience?.Remove(Context.ConnectionId, $"ticket-{ticketId}");
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"ticket-{ticketId}");
    }

    /// <summary>
    /// Join the global ticket list feed.
    /// </summary>
    public async Task JoinTicketList()
    {
        if (Restricted) { await SubscribeAsync("ticket-list", staffOnly: true); return; }
        await Groups.AddToGroupAsync(Context.ConnectionId, "ticket-list");
    }

    /// <summary>
    /// Leave the global ticket list feed.
    /// </summary>
    public async Task LeaveTicketList()
    {
        audience?.Remove(Context.ConnectionId, "ticket-list");
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "ticket-list");
    }

    /// <summary>
    /// Broadcast a presence ping for a ticket (agent is viewing it).
    /// </summary>
    public async Task Presence(int ticketId, string userName)
    {
        if (Restricted)
        {
            await SubscribeAsync($"ticket-{ticketId}", ticketId);
            await broadcasts!.SendAsync(ticketId, $"ticket-{ticketId}", "PresenceUpdate", new { ticketId, userName, connectionId = Context.ConnectionId }, Context.ConnectionId);
            return;
        }
        await Clients.OthersInGroup($"ticket-{ticketId}")
            .SendAsync("PresenceUpdate", new { ticketId, userName, connectionId = Context.ConnectionId });
    }

    // ── Live Chat ──

    /// <summary>
    /// Join the chat queue feed to receive notifications of new chat sessions.
    /// </summary>
    public async Task JoinChatQueue()
    {
        if (Restricted) { await SubscribeAsync("chat-queue", staffOnly: true); return; }
        await Groups.AddToGroupAsync(Context.ConnectionId, "chat-queue");
    }

    /// <summary>
    /// Leave the chat queue feed.
    /// </summary>
    public async Task LeaveChatQueue()
    {
        audience?.Remove(Context.ConnectionId, "chat-queue");
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "chat-queue");
    }

    /// <summary>
    /// Join a specific chat session room to receive live messages.
    /// </summary>
    public async Task JoinChat(int sessionId)
    {
        if (Restricted)
        {
            var ticketId = await TicketAccessScope.WithoutVisibilityAsync(() => db!.ChatSessions.Where(session => session.Id == sessionId).Select(session => (int?)session.TicketId).FirstOrDefaultAsync());
            if (ticketId is null) throw new HubException("Chat not found.");
            await SubscribeAsync($"chat-{sessionId}", ticketId);
            return;
        }
        await Groups.AddToGroupAsync(Context.ConnectionId, $"chat-{sessionId}");
    }

    /// <summary>
    /// Leave a chat session room.
    /// </summary>
    public async Task LeaveChat(int sessionId)
    {
        audience?.Remove(Context.ConnectionId, $"chat-{sessionId}");
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"chat-{sessionId}");
    }

    /// <summary>
    /// Broadcast a typing indicator within a chat session.
    /// </summary>
    public async Task ChatTyping(int sessionId, string userName)
    {
        if (Restricted)
        {
            await JoinChat(sessionId);
            var ticketId = await TicketAccessScope.WithoutVisibilityAsync(() => db!.ChatSessions.Where(session => session.Id == sessionId).Select(session => session.TicketId).SingleAsync());
            await broadcasts!.SendAsync(ticketId, $"chat-{sessionId}", "ChatTyping", new { sessionId, userName }, Context.ConnectionId);
            return;
        }
        await Clients.OthersInGroup($"chat-{sessionId}")
            .SendAsync("ChatTyping", new { sessionId, userName });
    }
}
