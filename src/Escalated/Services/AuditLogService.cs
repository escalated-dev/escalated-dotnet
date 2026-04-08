using System.Text.Json;
using Escalated.Data;
using Escalated.Models;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Services;

public class AuditLogService
{
    private readonly EscalatedDbContext _db;

    public AuditLogService(EscalatedDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Log an audit entry for an entity mutation.
    /// </summary>
    public async Task LogAsync(string entityType, int entityId, string action, int? userId = null,
        object? oldValues = null, object? newValues = null, string? ipAddress = null,
        string? userAgent = null, CancellationToken ct = default)
    {
        var entry = new AuditLog
        {
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            UserId = userId,
            OldValues = oldValues != null ? JsonSerializer.Serialize(oldValues) : null,
            NewValues = newValues != null ? JsonSerializer.Serialize(newValues) : null,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            CreatedAt = DateTime.UtcNow
        };

        _db.AuditLogs.Add(entry);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Query audit logs for a specific entity.
    /// </summary>
    public async Task<List<AuditLog>> GetForEntityAsync(string entityType, int entityId,
        int limit = 50, CancellationToken ct = default)
    {
        return await _db.AuditLogs
            .Where(a => a.EntityType == entityType && a.EntityId == entityId)
            .OrderByDescending(a => a.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Query all audit logs with optional filters.
    /// </summary>
    public async Task<(List<AuditLog> Items, int TotalCount)> ListAsync(
        int? userId = null, string? entityType = null, string? action = null,
        int page = 1, int perPage = 50, CancellationToken ct = default)
    {
        var query = _db.AuditLogs.AsQueryable();

        if (userId.HasValue) query = query.Where(a => a.UserId == userId.Value);
        if (!string.IsNullOrEmpty(entityType)) query = query.Where(a => a.EntityType == entityType);
        if (!string.IsNullOrEmpty(action)) query = query.Where(a => a.Action == action);

        query = query.OrderByDescending(a => a.CreatedAt);

        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * perPage).Take(perPage).ToListAsync(ct);

        return (items, total);
    }

    /// <summary>
    /// Purge audit logs older than a specified number of days.
    /// </summary>
    public async Task<int> PurgeAsync(int daysToKeep = 90, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-daysToKeep);
        var old = await _db.AuditLogs.Where(a => a.CreatedAt < cutoff).ToListAsync(ct);
        _db.AuditLogs.RemoveRange(old);
        await _db.SaveChangesAsync(ct);
        return old.Count;
    }
}
