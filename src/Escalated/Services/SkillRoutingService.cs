using Escalated.Data;
using Escalated.Enums;
using Escalated.Models;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Services;

public class SkillRoutingService
{
    private readonly EscalatedDbContext _db;

    public SkillRoutingService(EscalatedDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Find agents with skills matching ticket tags, sorted by current workload (ascending).
    /// </summary>
    public async Task<List<int>> FindMatchingAgentIdsAsync(Ticket ticket, CancellationToken ct = default)
    {
        // Get the ticket's tag names
        var tagNames = await _db.TicketTags
            .Where(tt => tt.TicketId == ticket.Id)
            .Join(_db.Tags, tt => tt.TagId, t => t.Id, (tt, t) => t.Name)
            .ToListAsync(ct);

        if (!tagNames.Any()) return new List<int>();

        // Find skills matching tag names
        var skillIds = await _db.Skills
            .Where(s => tagNames.Contains(s.Name))
            .Select(s => s.Id)
            .ToListAsync(ct);

        if (!skillIds.Any()) return new List<int>();

        // Find agents with these skills
        var agentIds = await _db.AgentSkills
            .Where(a => skillIds.Contains(a.SkillId))
            .Select(a => a.UserId)
            .Distinct()
            .ToListAsync(ct);

        if (!agentIds.Any()) return new List<int>();

        // Sort by open ticket count (ascending)
        var agentLoads = new List<(int UserId, int OpenCount)>();
        foreach (var agentId in agentIds)
        {
            var openCount = await _db.Tickets
                .Where(t => t.AssignedTo == agentId)
                .Where(t => t.Status != TicketStatus.Resolved && t.Status != TicketStatus.Closed)
                .CountAsync(ct);
            agentLoads.Add((agentId, openCount));
        }

        return agentLoads
            .OrderBy(a => a.OpenCount)
            .Select(a => a.UserId)
            .ToList();
    }

    /// <summary>
    /// Auto-assign ticket to the best matching agent by skills.
    /// </summary>
    public async Task<int?> FindBestAgentAsync(Ticket ticket, CancellationToken ct = default)
    {
        var agents = await FindMatchingAgentIdsAsync(ticket, ct);
        return agents.FirstOrDefault();
    }
}
