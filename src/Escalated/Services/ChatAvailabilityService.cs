using Escalated.Data;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Services;

/// <summary>
/// Checks whether live chat is currently available by looking at
/// agent online status and business hours configuration.
/// </summary>
public class ChatAvailabilityService
{
    private readonly EscalatedDbContext _db;
    private readonly BusinessHoursCalculator _businessHours;

    public ChatAvailabilityService(EscalatedDbContext db, BusinessHoursCalculator businessHours)
    {
        _db = db;
        _businessHours = businessHours;
    }

    /// <summary>
    /// Returns true if at least one agent is available and the current
    /// time falls within business hours (when configured).
    /// </summary>
    public async Task<bool> IsAvailableAsync(int? departmentId = null, CancellationToken ct = default)
    {
        // Check if any agents have capacity for the chat channel
        var agentQuery = _db.AgentCapacities
            .Where(c => c.Channel == "chat" && c.CurrentCount < c.MaxCapacity);

        if (departmentId.HasValue)
        {
            // Filter to agents in the specified department
            var agentIdsInDept = _db.AgentProfiles
                .Where(a => a.DepartmentId == departmentId)
                .Select(a => a.UserId);

            agentQuery = agentQuery.Where(c => agentIdsInDept.Contains(c.UserId));
        }

        var hasAvailableAgent = await agentQuery.AnyAsync(ct);
        if (!hasAvailableAgent)
            return false;

        // Check business hours if configured
        var isWithinHours = await _businessHours.IsWithinBusinessHoursAsync(ct);

        return isWithinHours;
    }

    /// <summary>
    /// Get the number of chats waiting in queue.
    /// </summary>
    public async Task<int> GetQueueDepthAsync(int? departmentId = null, CancellationToken ct = default)
    {
        var query = _db.ChatSessions.Where(s => s.Status == "waiting");

        if (departmentId.HasValue)
            query = query.Where(s => s.DepartmentId == departmentId);

        return await query.CountAsync(ct);
    }
}
