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
    /// Rank agents eligible under explicit routing (tags / departments): they must hold
    /// <i>every</i> matching skill row. Ranking is proficiency sum (required skills only)
    /// descending, then current open workload ascending.
    /// </summary>
    public async Task<List<string>> FindMatchingAgentIdsAsync(Ticket ticket, CancellationToken ct = default)
    {
        var tagIds = await _db.TicketTags
            .Where(tt => tt.TicketId == ticket.Id)
            .Select(tt => tt.TagId)
            .Distinct()
            .ToListAsync(ct);

        var skillFromTags = tagIds.Count == 0
            ? new List<int>()
            : await _db.SkillRoutingTags
                .Where(rt => tagIds.Contains(rt.TagId))
                .Select(rt => rt.SkillId)
                .Distinct()
                .ToListAsync(ct);

        var skillFromDept = new List<int>();
        if (ticket.DepartmentId is { } deptId)
        {
            skillFromDept = await _db.SkillRoutingDepartments
                .Where(rd => rd.DepartmentId == deptId)
                .Select(rd => rd.SkillId)
                .Distinct()
                .ToListAsync(ct);
        }

        var requiredSkillIds = skillFromTags.Union(skillFromDept).Distinct().ToList();
        var requiredCount = requiredSkillIds.Count;
        if (requiredCount == 0)
        {
            return new List<string>();
        }

        var candidateRows = await _db.AgentSkills
            .AsNoTracking()
            .Where(a => requiredSkillIds.Contains(a.SkillId))
            .GroupBy(a => a.UserId)
            .Where(g => g.Select(a => a.SkillId).Distinct().Count() == requiredCount)
            .Select(g => new
            {
                UserId = g.Key,
                ProficiencySum = g.Sum(a => a.Proficiency),
            })
            .ToListAsync(ct);

        if (candidateRows.Count == 0)
        {
            return new List<string>();
        }

        var userIds = candidateRows.Select(r => r.UserId).ToList();

        var openCounts = await _db.Tickets
            .Where(t =>
                t.AssignedTo != null && userIds.Contains(t.AssignedTo)
                                         && t.Status != TicketStatus.Resolved
                                         && t.Status != TicketStatus.Closed)
            .GroupBy(t => t.AssignedTo!)
            .Select(g => new { UserId = g.Key, Open = g.Count() })
            .ToListAsync(ct);

        var loads = openCounts.ToDictionary(x => x.UserId, x => x.Open);

        return candidateRows
            .OrderByDescending(r => r.ProficiencySum)
            .ThenBy(r => loads.GetValueOrDefault(r.UserId, 0))
            .ThenBy(r => r.UserId)
            .Select(r => r.UserId)
            .ToList();
    }

    /// <summary>Best match (same ordering as list), or <c>null</c> when no eligible router.</summary>
    public async Task<string?> FindBestAgentAsync(Ticket ticket, CancellationToken ct = default)
    {
        var agents = await FindMatchingAgentIdsAsync(ticket, ct);
        return agents.Count == 0 ? null : agents[0];
    }
}
