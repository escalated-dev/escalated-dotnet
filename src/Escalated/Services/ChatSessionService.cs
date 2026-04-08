using Escalated.Configuration;
using Escalated.Data;
using Escalated.Enums;
using Escalated.Events;
using Escalated.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Escalated.Services;

/// <summary>
/// Manages live-chat session lifecycle: creation, acceptance by an agent,
/// sending messages (stored as ticket replies), and ending sessions.
/// </summary>
public class ChatSessionService
{
    private readonly EscalatedDbContext _db;
    private readonly TicketService _ticketService;
    private readonly ChatRoutingService _routing;
    private readonly IEscalatedEventDispatcher _events;
    private readonly EscalatedOptions _options;

    public ChatSessionService(
        EscalatedDbContext db,
        TicketService ticketService,
        ChatRoutingService routing,
        IEscalatedEventDispatcher events,
        IOptions<EscalatedOptions> options)
    {
        _db = db;
        _ticketService = ticketService;
        _routing = routing;
        _events = events;
        _options = options.Value;
    }

    /// <summary>
    /// Start a new chat session. Creates an underlying ticket with status Live
    /// and channel "chat", then applies routing rules to determine the agent
    /// or department.
    /// </summary>
    public async Task<ChatSession> StartAsync(
        string visitorName,
        string? visitorEmail = null,
        string? initialMessage = null,
        int? departmentId = null,
        CancellationToken ct = default)
    {
        // Create the backing ticket
        var ticket = await _ticketService.CreateAsync(
            subject: $"Chat with {visitorName}",
            description: initialMessage,
            guestName: visitorName,
            guestEmail: visitorEmail,
            departmentId: departmentId,
            ticketType: "chat",
            ct: ct);

        // Override channel on the ticket for chat
        ticket.TicketType = "chat";
        _db.Tickets.Update(ticket);

        var session = new ChatSession
        {
            TicketId = ticket.Id,
            VisitorName = visitorName,
            VisitorEmail = visitorEmail,
            DepartmentId = departmentId,
            Status = "waiting",
            LastActivityAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // Apply routing rules
        var route = await _routing.ResolveAsync(departmentId, ct);
        if (route.DepartmentId.HasValue)
        {
            session.DepartmentId = route.DepartmentId;
            ticket.DepartmentId = route.DepartmentId;
            _db.Tickets.Update(ticket);
        }
        if (route.AgentId.HasValue)
        {
            session.AgentId = route.AgentId;
            session.Status = "active";
            session.AcceptedAt = DateTime.UtcNow;
            ticket.AssignedTo = route.AgentId;
            _db.Tickets.Update(ticket);
        }

        _db.ChatSessions.Add(session);
        await _db.SaveChangesAsync(ct);

        // Add the initial message as a reply if provided
        if (!string.IsNullOrWhiteSpace(initialMessage))
        {
            await _ticketService.AddReplyAsync(ticket, initialMessage, authorType: "visitor", ct: ct);
        }

        await _events.DispatchAsync(new ChatSessionStartedEvent(session), ct);

        return session;
    }

    /// <summary>
    /// Agent accepts a waiting chat session.
    /// </summary>
    public async Task<ChatSession> AcceptAsync(int sessionId, int agentId, CancellationToken ct = default)
    {
        var session = await FindByIdAsync(sessionId, ct)
            ?? throw new InvalidOperationException("Chat session not found.");

        if (session.Status != "waiting")
            throw new InvalidOperationException("Chat session is not in a waiting state.");

        session.AgentId = agentId;
        session.Status = "active";
        session.AcceptedAt = DateTime.UtcNow;
        session.UpdatedAt = DateTime.UtcNow;
        _db.ChatSessions.Update(session);

        // Assign the ticket to the agent
        var ticket = await _db.Tickets.FindAsync(new object[] { session.TicketId }, ct);
        if (ticket != null)
        {
            ticket.AssignedTo = agentId;
            ticket.UpdatedAt = DateTime.UtcNow;
            _db.Tickets.Update(ticket);
        }

        await _db.SaveChangesAsync(ct);

        await _events.DispatchAsync(new ChatSessionAcceptedEvent(session, agentId), ct);

        return session;
    }

    /// <summary>
    /// Send a message within a chat session. The message is stored as a
    /// reply on the underlying ticket.
    /// </summary>
    public async Task<Reply> SendMessageAsync(
        int sessionId,
        string body,
        int? authorId = null,
        string authorType = "visitor",
        CancellationToken ct = default)
    {
        var session = await FindByIdAsync(sessionId, ct)
            ?? throw new InvalidOperationException("Chat session not found.");

        if (session.Status == "ended")
            throw new InvalidOperationException("Chat session has ended.");

        var ticket = await _db.Tickets.FindAsync(new object[] { session.TicketId }, ct)
            ?? throw new InvalidOperationException("Underlying ticket not found.");

        var reply = await _ticketService.AddReplyAsync(ticket, body, authorId, authorType, ct: ct);

        session.LastActivityAt = DateTime.UtcNow;
        session.UpdatedAt = DateTime.UtcNow;
        _db.ChatSessions.Update(session);
        await _db.SaveChangesAsync(ct);

        return reply;
    }

    /// <summary>
    /// End a chat session. The underlying ticket is transitioned to Resolved.
    /// </summary>
    public async Task<ChatSession> EndAsync(int sessionId, int? causerId = null, CancellationToken ct = default)
    {
        var session = await FindByIdAsync(sessionId, ct)
            ?? throw new InvalidOperationException("Chat session not found.");

        if (session.Status == "ended")
            throw new InvalidOperationException("Chat session has already ended.");

        session.Status = "ended";
        session.EndedAt = DateTime.UtcNow;
        session.UpdatedAt = DateTime.UtcNow;
        _db.ChatSessions.Update(session);

        // Resolve the underlying ticket
        var ticket = await _db.Tickets.FindAsync(new object[] { session.TicketId }, ct);
        if (ticket != null)
        {
            await _ticketService.ChangeStatusAsync(ticket, TicketStatus.Resolved, causerId, ct);
        }

        await _db.SaveChangesAsync(ct);

        await _events.DispatchAsync(new ChatSessionEndedEvent(session, causerId), ct);

        return session;
    }

    public async Task<ChatSession?> FindByIdAsync(int id, CancellationToken ct = default)
    {
        return await _db.ChatSessions.FindAsync(new object[] { id }, ct);
    }

    public async Task<ChatSession?> FindByTicketIdAsync(int ticketId, CancellationToken ct = default)
    {
        return await _db.ChatSessions.FirstOrDefaultAsync(s => s.TicketId == ticketId, ct);
    }

    public async Task<List<ChatSession>> GetWaitingSessionsAsync(CancellationToken ct = default)
    {
        return await _db.ChatSessions
            .Where(s => s.Status == "waiting")
            .OrderBy(s => s.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<List<ChatSession>> GetActiveSessionsForAgentAsync(int agentId, CancellationToken ct = default)
    {
        return await _db.ChatSessions
            .Where(s => s.AgentId == agentId && s.Status == "active")
            .OrderByDescending(s => s.LastActivityAt)
            .ToListAsync(ct);
    }
}

// Chat events
public record ChatSessionStartedEvent(ChatSession Session);
public record ChatSessionAcceptedEvent(ChatSession Session, int AgentId);
public record ChatSessionEndedEvent(ChatSession Session, int? CauserId);
