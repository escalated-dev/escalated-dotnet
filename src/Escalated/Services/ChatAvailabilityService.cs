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
            .Where(c => c.Channel == "chat" && c.CurrentCount < c.MaxConcurrent);

        var hasAvailableAgent = await agentQuery.AnyAsync(ct);
        if (!hasAvailableAgent)
            return false;

        // Check business hours if configured
        var schedule = await _db.BusinessSchedules
            .Include(s => s.Holidays)
            .FirstOrDefaultAsync(ct);

        if (schedule != null)
        {
            return _businessHours.IsWithinBusinessHours(DateTime.UtcNow, schedule);
        }

        return true;
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
