using System.Security.Cryptography;
using System.Text;
using Escalated.Authorization;
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
    private readonly TicketAccess? _access;
    private readonly TicketService _ticketService;
    private readonly ChatRoutingService _routing;
    private readonly IEscalatedEventDispatcher _events;
    private readonly EscalatedOptions _options;

    public ChatSessionService(
        EscalatedDbContext db,
        TicketService ticketService,
        ChatRoutingService routing,
        IEscalatedEventDispatcher events,
        IOptions<EscalatedOptions> options, TicketAccess? access = null)
    {
        _db = db;
        _access = access;
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
        CancellationToken ct = default,
        string? metadata = null)
    {
        // Create the backing ticket
        var ticket = await _ticketService.CreateAsync(
            subject: $"Chat with {visitorName}",
            description: initialMessage,
            guestName: visitorName,
            guestEmail: visitorEmail,
            departmentId: departmentId,
            ticketType: "chat",
            ct: ct,
            metadata: metadata);

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
            Metadata = metadata,
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
        await _db.SaveChangesAsync(ct);
        if (!string.IsNullOrEmpty(route.AgentId) && (_access is null || await _access.AgentCanViewAsync(route.AgentId, ticket.Id, ct)))
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
        if (!string.IsNullOrEmpty(session.AgentId))
        {
            // Routing handed the chat straight to an agent: announce it as `chat.assigned`.
            await _events.DispatchAsync(new ChatSessionAcceptedEvent(session, session.AgentId), ct);
        }

        return session;
    }

    /// <summary>
    /// Agent accepts a waiting chat session.
    /// </summary>
    public async Task<ChatSession> AcceptAsync(int sessionId, string agentId, CancellationToken ct = default)
    {
        var session = await FindByIdAsync(sessionId, ct)
            ?? throw new InvalidOperationException("Chat session not found.");

        if (session.Status != "waiting")
            throw new InvalidOperationException("Chat session is not in a waiting state.");

        if (_access is not null && !await _access.AgentCanViewAsync(agentId, session.TicketId, ct)) throw new TicketAccessDeniedException();
        session.AgentId = agentId;
        session.Status = "active";
        session.AcceptedAt = DateTime.UtcNow;
        session.UpdatedAt = DateTime.UtcNow;
        _db.ChatSessions.Update(session);

        // Assign the ticket to the agent
        var ticket = await _db.Tickets.FirstOrDefaultAsync(ticket => ticket.Id == session.TicketId, ct);
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
    /// Hand a chat to another agent and/or department, as escalated-laravel's
    /// <c>ChatSessionService::transferChat</c>. With an agent, the session and
    /// its ticket are reassigned to that agent. With a department, the ticket
    /// moves there; without an agent too, the chat goes back to the queue
    /// (unassigned, waiting) and routing picks an agent for its new
    /// department. Announces <c>chat.transferred</c> with the previous agent,
    /// the new agent and the new department.
    /// </summary>
    /// <exception cref="InvalidOperationException">Unknown session.</exception>
    /// <exception cref="ChatTransferRefusedException">No target, an ended chat, an unknown
    /// department, or (under restricted visibility) an agent who is not staff or cannot see the ticket.</exception>
    public async Task<ChatSession> TransferAsync(int sessionId, string? toAgentId, int? toDepartmentId, CancellationToken ct = default)
    {
        var session = await FindByIdAsync(sessionId, ct)
            ?? throw new InvalidOperationException("Chat session not found.");

        if (session.Status == "ended")
            throw new ChatTransferRefusedException("Chat session has already ended.");
        if (string.IsNullOrEmpty(toAgentId) && toDepartmentId is null)
            throw new ChatTransferRefusedException("Provide an agent_id or department_id.");
        if (toDepartmentId is { } departmentId && !await _db.Departments.AnyAsync(d => d.Id == departmentId, ct))
            throw new ChatTransferRefusedException("The selected department_id is invalid.");

        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == session.TicketId, ct)
            ?? throw new InvalidOperationException("Underlying ticket not found.");
        var fromAgentId = session.AgentId;
        var previousDepartmentId = ticket.DepartmentId;

        if (toDepartmentId is not null)
        {
            ticket.DepartmentId = toDepartmentId;
            ticket.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        // Under restricted visibility the agent must be staff who can see the
        // ticket in its new department (as AcceptAsync checks).
        if (!string.IsNullOrEmpty(toAgentId) && _access is not null && !await _access.AgentCanViewAsync(toAgentId, ticket.Id, ct))
        {
            if (toDepartmentId is not null)
            {
                ticket.DepartmentId = previousDepartmentId;
                await _db.SaveChangesAsync(ct);
            }
            throw new ChatTransferRefusedException("Agent cannot access this ticket.");
        }

        if (toDepartmentId is not null)
        {
            session.DepartmentId = toDepartmentId;
        }
        if (!string.IsNullOrEmpty(toAgentId))
        {
            session.AgentId = toAgentId;
            ticket.AssignedTo = toAgentId;
        }
        else
        {
            session.AgentId = null;
            session.Status = "waiting";
            session.AcceptedAt = null;
            ticket.AssignedTo = null;
        }
        session.UpdatedAt = DateTime.UtcNow;
        ticket.UpdatedAt = DateTime.UtcNow;
        _db.ChatSessions.Update(session);
        _db.Tickets.Update(ticket);
        await _db.SaveChangesAsync(ct);

        await _events.DispatchAsync(new ChatSessionTransferredEvent(session, fromAgentId, string.IsNullOrEmpty(toAgentId) ? null : toAgentId, toDepartmentId), ct);

        if (string.IsNullOrEmpty(toAgentId))
        {
            var route = await _routing.ResolveAsync(toDepartmentId, ct);
            if (!string.IsNullOrEmpty(route.AgentId) && (_access is null || await _access.AgentCanViewAsync(route.AgentId, ticket.Id, ct)))
            {
                session = await AcceptAsync(session.Id, route.AgentId, ct);
            }
        }

        return session;
    }

    /// <summary>
    /// Send a message within a chat session. The message is stored as a
    /// reply on the underlying ticket.
    /// </summary>
    public async Task<Reply> SendMessageAsync(
        int sessionId,
        string body,
        string? authorId = null,
        string authorType = "visitor",
        CancellationToken ct = default)
    {
        var session = await FindByIdAsync(sessionId, ct)
            ?? throw new InvalidOperationException("Chat session not found.");

        if (session.Status == "ended")
            throw new InvalidOperationException("Chat session has ended.");

        var ticket = await _db.Tickets.FirstOrDefaultAsync(ticket => ticket.Id == session.TicketId, ct)
            ?? throw new InvalidOperationException("Underlying ticket not found.");

        var reply = await _ticketService.AddReplyAsync(ticket, body, authorId, authorType, ct: ct);

        session.LastActivityAt = DateTime.UtcNow;
        session.UpdatedAt = DateTime.UtcNow;
        _db.ChatSessions.Update(session);
        await _db.SaveChangesAsync(ct);

        await _events.DispatchAsync(new ChatMessageEvent(session, reply, authorType), ct);

        return reply;
    }

    /// <summary>
    /// End a chat session. The underlying ticket is transitioned to Resolved.
    /// </summary>
    public async Task<ChatSession> EndAsync(int sessionId, string? causerId = null, CancellationToken ct = default)
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
        var ticket = await _db.Tickets.FirstOrDefaultAsync(ticket => ticket.Id == session.TicketId, ct);
        if (ticket != null)
        {
            await _ticketService.ChangeStatusAsync(ticket, TicketStatus.Resolved, causerId, ct);
        }

        await _db.SaveChangesAsync(ct);

        await _events.DispatchAsync(new ChatSessionEndedEvent(session, causerId), ct);

        return session;
    }

    /// <summary>
    /// Give a chat a new random visitor token (256 bits, base64url) and store
    /// only its SHA-256. The widget reaches the chat through this token alone,
    /// until it expires (<see cref="VisitorTokenLifetime"/> from now), as
    /// escalated-laravel's chat grant does.
    /// </summary>
    public async Task<string> IssueVisitorTokenAsync(ChatSession session, CancellationToken ct = default)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        session.VisitorTokenHash = HashVisitorToken(token);
        session.VisitorTokenExpiresAt = VisitorTokenExpiry();
        session.UpdatedAt = DateTime.UtcNow;
        _db.ChatSessions.Update(session);
        await _db.SaveChangesAsync(ct);
        return token;
    }

    /// <summary>
    /// The chat an unexpired visitor token belongs to, or null (404). A chat
    /// started before tokens expired has no expiry yet; this request gives it a
    /// full lifetime, so chats in progress during an upgrade keep working.
    /// </summary>
    public async Task<ChatSession?> FindByVisitorTokenAsync(string? token, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 128) return null;
        var hash = HashVisitorToken(token);
        var session = await _db.ChatSessions.FirstOrDefaultAsync(session => session.VisitorTokenHash == hash, ct);
        if (session == null) return null;
        if (session.VisitorTokenExpiresAt == null)
        {
            session.VisitorTokenExpiresAt = VisitorTokenExpiry();
            await _db.SaveChangesAsync(ct);
        }
        else if (session.VisitorTokenExpiresAt <= DateTime.UtcNow)
        {
            return null;
        }

        return session;
    }

    /// <summary>
    /// How long a visitor's chat token works: the guest access grant lifetime
    /// (<c>Escalated:GuestAccess:TtlMinutes</c>, clamped to 5 minutes..7 days).
    /// </summary>
    public TimeSpan VisitorTokenLifetime => TimeSpan.FromMinutes(Math.Clamp(_options.GuestAccess.TtlMinutes, 5, 7 * 24 * 60));

    private DateTime VisitorTokenExpiry()
    {
        var now = DateTime.UtcNow;
        return new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc).Add(VisitorTokenLifetime);
    }

    public static string HashVisitorToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    public async Task<ChatSession?> FindByIdAsync(int id, CancellationToken ct = default)
    {
        return await _db.ChatSessions.FirstOrDefaultAsync(session => session.Id == id, ct);
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

    public async Task<List<ChatSession>> GetActiveSessionsForAgentAsync(string agentId, CancellationToken ct = default)
    {
        return await _db.ChatSessions
            .Where(s => s.AgentId == agentId && s.Status == "active")
            .OrderByDescending(s => s.LastActivityAt)
            .ToListAsync(ct);
    }
}

// Chat events
public record ChatSessionStartedEvent(ChatSession Session);
public record ChatSessionAcceptedEvent(ChatSession Session, string AgentId);
public record ChatSessionEndedEvent(ChatSession Session, string? CauserId);

/// <summary>A chat was handed to another agent and/or department (webhook and trigger name <c>chat.transferred</c>).</summary>
public record ChatSessionTransferredEvent(ChatSession Session, string? FromAgentId, string? ToAgentId, int? ToDepartmentId);

/// <summary>A chat transfer that cannot be made; the agent chat API answers 422.</summary>
public class ChatTransferRefusedException : Exception
{
    public ChatTransferRefusedException(string message) : base(message)
    {
    }
}

/// <summary>A message was posted in a live chat (webhook and trigger name <c>chat.message</c>).</summary>
public record ChatMessageEvent(ChatSession Session, Reply Reply, string AuthorType);
