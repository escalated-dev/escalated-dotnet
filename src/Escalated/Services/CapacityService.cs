using Escalated.Data;
using Escalated.Models;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Services;

public class CapacityService
{
    private readonly EscalatedDbContext _db;

    public CapacityService(EscalatedDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Check if an agent can accept a new ticket on a given channel.
    /// </summary>
    public async Task<bool> CanAcceptTicketAsync(int userId, string channel = "default",
        CancellationToken ct = default)
    {
        var capacity = await GetOrCreateAsync(userId, channel, ct);
        return capacity.HasCapacity();
    }

    /// <summary>
    /// Increment the agent's current load.
    /// </summary>
    public async Task IncrementLoadAsync(int userId, string channel = "default",
        CancellationToken ct = default)
    {
        var capacity = await GetOrCreateAsync(userId, channel, ct);
        capacity.CurrentCount++;
        capacity.UpdatedAt = DateTime.UtcNow;
        _db.AgentCapacities.Update(capacity);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Decrement the agent's current load.
    /// </summary>
    public async Task DecrementLoadAsync(int userId, string channel = "default",
        CancellationToken ct = default)
    {
        var capacity = await GetOrCreateAsync(userId, channel, ct);
        if (capacity.CurrentCount > 0)
        {
            capacity.CurrentCount--;
            capacity.UpdatedAt = DateTime.UtcNow;
            _db.AgentCapacities.Update(capacity);
            await _db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Get all agent capacities.
    /// </summary>
    public async Task<List<AgentCapacity>> GetAllCapacitiesAsync(CancellationToken ct = default)
    {
        return await _db.AgentCapacities.ToListAsync(ct);
    }

    /// <summary>
    /// Set the max concurrent tickets for an agent on a channel.
    /// </summary>
    public async Task SetMaxConcurrentAsync(int userId, int maxConcurrent, string channel = "default",
        CancellationToken ct = default)
    {
        var capacity = await GetOrCreateAsync(userId, channel, ct);
        capacity.MaxConcurrent = maxConcurrent;
        capacity.UpdatedAt = DateTime.UtcNow;
        _db.AgentCapacities.Update(capacity);
        await _db.SaveChangesAsync(ct);
    }

    private async Task<AgentCapacity> GetOrCreateAsync(int userId, string channel, CancellationToken ct)
    {
        var capacity = await _db.AgentCapacities
            .FirstOrDefaultAsync(a => a.UserId == userId && a.Channel == channel, ct);

        if (capacity == null)
        {
            capacity = new AgentCapacity
            {
                UserId = userId,
                Channel = channel,
                MaxConcurrent = 10,
                CurrentCount = 0,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _db.AgentCapacities.Add(capacity);
            await _db.SaveChangesAsync(ct);
        }

        return capacity;
    }
}
