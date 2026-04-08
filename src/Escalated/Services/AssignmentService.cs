using Escalated.Data;
using Escalated.Enums;
using Escalated.Events;
using Escalated.Models;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Services;

public class AssignmentService
{
    private readonly EscalatedDbContext _db;
    private readonly IEscalatedEventDispatcher _events;
    private readonly TicketService _ticketService;

    public AssignmentService(EscalatedDbContext db, IEscalatedEventDispatcher events, TicketService ticketService)
    {
        _db = db;
        _events = events;
        _ticketService = ticketService;
    }

    public async Task<Ticket> AssignAsync(Ticket ticket, int agentId, int? causerId = null,
        CancellationToken ct = default)
    {
        ticket.AssignedTo = agentId;
        ticket.UpdatedAt = DateTime.UtcNow;
        _db.Tickets.Update(ticket);

        await _ticketService.LogActivityAsync(ticket, ActivityType.Assigned, causerId,
            new Dictionary<string, object> { ["agent_id"] = agentId }, ct);

        await _db.SaveChangesAsync(ct);
        await _events.DispatchAsync(new TicketAssignedEvent(ticket, agentId, causerId), ct);
        return ticket;
    }

    public async Task<Ticket> UnassignAsync(Ticket ticket, int? causerId = null,
        CancellationToken ct = default)
    {
        var previousAgentId = ticket.AssignedTo;
        ticket.AssignedTo = null;
        ticket.UpdatedAt = DateTime.UtcNow;
        _db.Tickets.Update(ticket);

        await _ticketService.LogActivityAsync(ticket, ActivityType.Unassigned, causerId,
            new Dictionary<string, object> { ["previous_agent_id"] = previousAgentId ?? 0 }, ct);

        await _db.SaveChangesAsync(ct);
        await _events.DispatchAsync(new TicketUnassignedEvent(ticket, previousAgentId, causerId), ct);
        return ticket;
    }

    /// <summary>
    /// Auto-assign a ticket to the agent in its department with the fewest open tickets.
    /// </summary>
    public async Task<Ticket?> AutoAssignAsync(Ticket ticket, CancellationToken ct = default)
    {
        if (!ticket.DepartmentId.HasValue) return null;

        // Find agents in the department's role (simplified: uses agent_profiles)
        var agents = await _db.AgentProfiles
            .Select(a => a.UserId)
            .ToListAsync(ct);

        if (!agents.Any()) return null;

        // Find the agent with the fewest open tickets
        var agentLoads = new List<(int UserId, int OpenCount)>();
        foreach (var agentId in agents)
        {
            var count = await _db.Tickets
                .Where(t => t.AssignedTo == agentId)
                .Where(t => t.Status != TicketStatus.Resolved && t.Status != TicketStatus.Closed)
                .CountAsync(ct);
            agentLoads.Add((agentId, count));
        }

        var bestAgent = agentLoads.OrderBy(a => a.OpenCount).First();
        return await AssignAsync(ticket, bestAgent.UserId, null, ct);
    }

    public async Task<AgentWorkload> GetAgentWorkloadAsync(int agentId, CancellationToken ct = default)
    {
        var openCount = await _db.Tickets
            .Where(t => t.AssignedTo == agentId)
            .Where(t => t.Status != TicketStatus.Resolved && t.Status != TicketStatus.Closed)
            .CountAsync(ct);

        var resolvedToday = await _db.Tickets
            .Where(t => t.AssignedTo == agentId)
            .Where(t => t.ResolvedAt != null && t.ResolvedAt >= DateTime.UtcNow.Date)
            .CountAsync(ct);

        var breachedCount = await _db.Tickets
            .Where(t => t.AssignedTo == agentId)
            .Where(t => t.Status != TicketStatus.Resolved && t.Status != TicketStatus.Closed)
            .Where(t => t.SlaFirstResponseBreached || t.SlaResolutionBreached)
            .CountAsync(ct);

        return new AgentWorkload
        {
            Open = openCount,
            ResolvedToday = resolvedToday,
            SlaBreached = breachedCount
        };
    }
}

public class AgentWorkload
{
    public int Open { get; set; }
    public int ResolvedToday { get; set; }
    public int SlaBreached { get; set; }
}
