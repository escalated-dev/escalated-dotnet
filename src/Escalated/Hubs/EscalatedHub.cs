using Microsoft.AspNetCore.SignalR;
using Escalated.Authorization;
using Escalated.Data;
using Escalated.Services;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Hubs;

/// <summary>
/// SignalR hub for real-time ticket updates (opt-in via EscalatedOptions.EnableRealTime).
/// Clients join/leave ticket-specific groups to receive live updates.
///
/// <para>Every join is authorized, whatever the visibility mode. The ticket list and
/// chat queue are staff rooms: an anonymous or customer connection is refused. A ticket
/// room needs a user who may view that ticket, or a guest presenting the ticket's
/// <c>guest_token</c>. A chat room needs a user who may view the chat's ticket, or the
/// visitor presenting that chat's <c>chat_token</c> (or the ticket's <c>guest_token</c>).</para>
/// </summary>
public class EscalatedHub : Hub
{
    private readonly TicketAccess? access;
    private readonly EscalatedDbContext? db;
    private readonly TicketRealtimeAudience? audience;
    private readonly TicketBroadcastService? broadcasts;
    private readonly bool legacyGuestTokens;
    public EscalatedHub(TicketAccess? access = null, EscalatedDbContext? db = null, TicketRealtimeAudience? audience = null, TicketBroadcastService? broadcasts = null,
        Microsoft.Extensions.Options.IOptions<Escalated.Configuration.EscalatedOptions>? options = null)
    {
        this.access = access;
        this.db = db;
        this.audience = audience;
        this.broadcasts = broadcasts;
        legacyGuestTokens = options?.Value.GuestAccess.LegacyTokens == true;
    }

    private bool Restricted => access?.IsRestricted == true;

    /// <summary>Authorizes the caller for <paramref name="room"/>, then adds it.</summary>
    private async Task SubscribeAsync(string room, int? ticketId = null, bool staffOnly = false, int? chatSessionId = null)
    {
        // Without Escalated's services there is nothing to check against: fail closed.
        if (access is null || db is null) throw new HubException("Access denied.");

        var userId = access.UserId(Context.User);
        string? token = null;
        if (userId is not null)
        {
            if (staffOnly && !await access.IsAdminAsync(Context.User!) && !await access.IsAgentAsync(Context.User!)) throw new HubException("Access denied.");
            if (ticketId is not null && !await access.CanViewAsync(Context.User!, ticketId.Value)) throw new HubException("Ticket not found.");
        }
        else
        {
            if (staffOnly || ticketId is null) throw new HubException("Access denied.");
            var query = Context.GetHttpContext()?.Request.Query;
            token = query?["guest_token"].ToString();
            var chatToken = query?["chat_token"].ToString();
            var allowed = false;
            if (!string.IsNullOrEmpty(token))
                allowed = await TicketAccessScope.WithoutVisibilityAsync(() => db.Tickets.Where(GuestAccessService.Opens(token, legacyGuestTokens, DateTime.UtcNow)).AnyAsync(ticket => ticket.Id == ticketId));
            if (!allowed && chatSessionId is not null && !string.IsNullOrEmpty(chatToken))
            {
                var hash = ChatSessionService.HashVisitorToken(chatToken);
                allowed = await TicketAccessScope.WithoutVisibilityAsync(() => db.ChatSessions.AnyAsync(session => session.Id == chatSessionId && session.VisitorTokenHash == hash));
                if (allowed) token = null;
            }
            if (!allowed) throw new HubException("Access denied.");
        }

        if (Restricted)
        {
            audience!.Add(new TicketSubscription(Context.ConnectionId, room, userId, token, userId is null ? ticketId : null, staffOnly));
            return;
        }
        await Groups.AddToGroupAsync(Context.ConnectionId, room);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        audience?.RemoveConnection(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Join a ticket room to receive live updates for that ticket.
    /// </summary>
    public Task JoinTicket(int ticketId) => SubscribeAsync($"ticket-{ticketId}", ticketId);

    /// <summary>
    /// Leave a ticket room.
    /// </summary>
    public async Task LeaveTicket(int ticketId)
    {
        audience?.Remove(Context.ConnectionId, $"ticket-{ticketId}");
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"ticket-{ticketId}");
    }

    /// <summary>
    /// Join the global ticket list feed. Staff only.
    /// </summary>
    public Task JoinTicketList() => SubscribeAsync("ticket-list", staffOnly: true);

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
        await JoinTicket(ticketId);
        if (Restricted)
        {
            await broadcasts!.SendAsync(ticketId, $"ticket-{ticketId}", "PresenceUpdate", new { ticketId, userName, connectionId = Context.ConnectionId }, Context.ConnectionId);
            return;
        }
        await Clients.OthersInGroup($"ticket-{ticketId}")
            .SendAsync("PresenceUpdate", new { ticketId, userName, connectionId = Context.ConnectionId });
    }

    // ── Live Chat ──

    /// <summary>
    /// Join the chat queue feed to receive notifications of new chat sessions. Staff only.
    /// </summary>
    public Task JoinChatQueue() => SubscribeAsync("chat-queue", staffOnly: true);

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
        if (db is null) throw new HubException("Access denied.");
        var ticketId = await TicketAccessScope.WithoutVisibilityAsync(() => db.ChatSessions.Where(session => session.Id == sessionId).Select(session => (int?)session.TicketId).FirstOrDefaultAsync());
        if (ticketId is null) throw new HubException("Chat not found.");
        await SubscribeAsync($"chat-{sessionId}", ticketId, chatSessionId: sessionId);
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
        await JoinChat(sessionId);
        if (Restricted)
        {
            var ticketId = await TicketAccessScope.WithoutVisibilityAsync(() => db!.ChatSessions.Where(session => session.Id == sessionId).Select(session => session.TicketId).SingleAsync());
            await broadcasts!.SendAsync(ticketId, $"chat-{sessionId}", "ChatTyping", new { sessionId, userName }, Context.ConnectionId);
            return;
        }
        await Clients.OthersInGroup($"chat-{sessionId}")
            .SendAsync("ChatTyping", new { sessionId, userName });
    }
}
