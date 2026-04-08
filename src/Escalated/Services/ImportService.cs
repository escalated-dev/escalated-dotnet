using System.Text.Json;
using Escalated.Data;
using Escalated.Enums;
using Escalated.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Escalated.Services;

/// <summary>
/// Interface that import adapters (Zendesk, Freshdesk, etc.) must implement.
/// </summary>
public interface IImportAdapter
{
    string Name { get; }
    string[] EntityTypes { get; }
    Task<bool> TestConnectionAsync(string credentials, CancellationToken ct);
    Task<ImportBatch> ExtractAsync(string entityType, string credentials, string? cursor, CancellationToken ct);
}

public class ImportBatch
{
    public List<Dictionary<string, object?>> Records { get; set; } = new();
    public string? Cursor { get; set; }
    public int? TotalCount { get; set; }
    public bool IsExhausted => Cursor == null;
}

public class ImportService
{
    private readonly EscalatedDbContext _db;
    private readonly ILogger<ImportService> _logger;
    private readonly IEnumerable<IImportAdapter> _adapters;

    public ImportService(EscalatedDbContext db, ILogger<ImportService> logger,
        IEnumerable<IImportAdapter> adapters)
    {
        _db = db;
        _logger = logger;
        _adapters = adapters;
    }

    public IImportAdapter? ResolveAdapter(string platform)
    {
        return _adapters.FirstOrDefault(a => a.Name == platform);
    }

    public async Task<bool> TestConnectionAsync(ImportJob job, CancellationToken ct = default)
    {
        var adapter = ResolveAdapter(job.Platform);
        if (adapter == null)
            throw new InvalidOperationException($"No adapter found for platform '{job.Platform}'.");

        return await adapter.TestConnectionAsync(job.Credentials ?? "{}", ct);
    }

    /// <summary>
    /// Run the full import for a job.
    /// </summary>
    public async Task RunAsync(ImportJob job, Action<string, string>? onProgress = null,
        CancellationToken ct = default)
    {
        var adapter = ResolveAdapter(job.Platform);
        if (adapter == null)
        {
            job.Status = "failed";
            _db.ImportJobs.Update(job);
            await _db.SaveChangesAsync(ct);
            throw new InvalidOperationException($"No adapter found for platform '{job.Platform}'.");
        }

        if (job.Status != "importing")
            job.TransitionTo("importing");

        job.StartedAt ??= DateTime.UtcNow;
        _db.ImportJobs.Update(job);
        await _db.SaveChangesAsync(ct);

        foreach (var entityType in adapter.EntityTypes)
        {
            await _db.Entry(job).ReloadAsync(ct);
            if (job.Status == "paused") return;

            await ImportEntityTypeAsync(job, adapter, entityType, onProgress, ct);
        }

        await _db.Entry(job).ReloadAsync(ct);
        if (job.Status == "paused") return;

        job.Status = "completed";
        job.CompletedAt = DateTime.UtcNow;
        _db.ImportJobs.Update(job);
        await _db.SaveChangesAsync(ct);
    }

    private async Task ImportEntityTypeAsync(ImportJob job, IImportAdapter adapter,
        string entityType, Action<string, string>? onProgress, CancellationToken ct)
    {
        string? cursor = null;
        int processed = 0, skipped = 0, failed = 0;

        do
        {
            var batch = await adapter.ExtractAsync(entityType, job.Credentials ?? "{}", cursor, ct);

            foreach (var record in batch.Records)
            {
                var sourceId = record.GetValueOrDefault("source_id")?.ToString();
                if (string.IsNullOrEmpty(sourceId))
                {
                    failed++;
                    continue;
                }

                var alreadyImported = await _db.ImportSourceMaps.AnyAsync(m =>
                    m.ImportJobId == job.Id && m.EntityType == entityType && m.SourceId == sourceId, ct);

                if (alreadyImported)
                {
                    skipped++;
                    continue;
                }

                try
                {
                    var escalatedId = await PersistRecordAsync(job, entityType, record, ct);
                    _db.ImportSourceMaps.Add(new ImportSourceMap
                    {
                        ImportJobId = job.Id,
                        EntityType = entityType,
                        SourceId = sourceId,
                        EscalatedId = escalatedId,
                        CreatedAt = DateTime.UtcNow
                    });
                    await _db.SaveChangesAsync(ct);
                    processed++;
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogWarning("Import failed for {EntityType} {SourceId}: {Error}",
                        entityType, sourceId, ex.Message);
                }
            }

            cursor = batch.Cursor;
            onProgress?.Invoke(entityType, $"processed={processed}, skipped={skipped}, failed={failed}");

            await _db.Entry(job).ReloadAsync(ct);
            if (job.Status == "paused") return;

        } while (cursor != null);
    }

    private async Task<string> PersistRecordAsync(ImportJob job, string entityType,
        Dictionary<string, object?> record, CancellationToken ct)
    {
        return entityType switch
        {
            "tags" => await PersistTagAsync(record, ct),
            "departments" => await PersistDepartmentAsync(record, ct),
            "tickets" => await PersistTicketAsync(job, record, ct),
            "replies" => await PersistReplyAsync(job, record, ct),
            _ => throw new InvalidOperationException($"Unknown entity type: {entityType}")
        };
    }

    private async Task<string> PersistTagAsync(Dictionary<string, object?> record, CancellationToken ct)
    {
        var name = record.GetValueOrDefault("name")?.ToString() ?? "Unnamed";
        var slug = Tag.GenerateSlug(name);
        var tag = await _db.Tags.FirstOrDefaultAsync(t => t.Slug == slug, ct);
        if (tag == null)
        {
            tag = new Tag { Name = name, Slug = slug, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            _db.Tags.Add(tag);
            await _db.SaveChangesAsync(ct);
        }
        return tag.Id.ToString();
    }

    private async Task<string> PersistDepartmentAsync(Dictionary<string, object?> record, CancellationToken ct)
    {
        var name = record.GetValueOrDefault("name")?.ToString() ?? "Unnamed";
        var slug = Department.GenerateSlug(name);
        var dept = await _db.Departments.FirstOrDefaultAsync(d => d.Slug == slug, ct);
        if (dept == null)
        {
            dept = new Department { Name = name, Slug = slug, IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            _db.Departments.Add(dept);
            await _db.SaveChangesAsync(ct);
        }
        return dept.Id.ToString();
    }

    private async Task<string> PersistTicketAsync(ImportJob job, Dictionary<string, object?> record, CancellationToken ct)
    {
        var ticket = new Ticket
        {
            Subject = record.GetValueOrDefault("title")?.ToString() ?? "Imported ticket",
            Status = Enum.TryParse<TicketStatus>(record.GetValueOrDefault("status")?.ToString(), true, out var s) ? s : TicketStatus.Open,
            Priority = Enum.TryParse<TicketPriority>(record.GetValueOrDefault("priority")?.ToString(), true, out var p) ? p : TicketPriority.Medium,
            Reference = $"TEMP-{Guid.NewGuid()}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Tickets.Add(ticket);
        await _db.SaveChangesAsync(ct);
        ticket.Reference = ticket.GenerateReference("ESC");
        _db.Tickets.Update(ticket);
        await _db.SaveChangesAsync(ct);

        return ticket.Id.ToString();
    }

    private async Task<string> PersistReplyAsync(ImportJob job, Dictionary<string, object?> record, CancellationToken ct)
    {
        var ticketSourceId = record.GetValueOrDefault("ticket_source_id")?.ToString();
        if (string.IsNullOrEmpty(ticketSourceId))
            throw new InvalidOperationException("Reply missing ticket_source_id.");

        var map = await _db.ImportSourceMaps.FirstOrDefaultAsync(m =>
            m.ImportJobId == job.Id && m.EntityType == "tickets" && m.SourceId == ticketSourceId, ct);
        if (map == null) throw new InvalidOperationException("Parent ticket not found for reply.");

        var reply = new Reply
        {
            TicketId = int.Parse(map.EscalatedId),
            Body = record.GetValueOrDefault("body")?.ToString() ?? "",
            IsInternalNote = record.GetValueOrDefault("is_internal_note")?.ToString() == "true",
            Type = "reply",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Replies.Add(reply);
        await _db.SaveChangesAsync(ct);
        return reply.Id.ToString();
    }
}
