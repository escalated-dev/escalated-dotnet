using System.Text.Json;
using Escalated.Data;
using Escalated.Enums;
using Escalated.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Escalated.Services;

/// <summary>
/// A source the admin Import wizard can import from, as escalated-laravel's
/// <c>Contracts\ImportAdapter</c>. Escalated ships none: the host (or a plugin
/// package) registers its adapters in DI as <see cref="IImportAdapter"/> -- a
/// Zendesk or Freshdesk adapter, say. Credentials travel as a JSON object string.
///
/// <para>Records come back already normalized (<c>source_id</c>, <c>title</c>,
/// <c>requester_source_id</c>, <c>tag_source_ids</c>, <c>ticket_source_id</c>,
/// ...), as Laravel's adapters return them.</para>
/// </summary>
public interface IImportAdapter
{
    /// <summary>Unique slug, e.g. "zendesk".</summary>
    string Name { get; }

    /// <summary>Importable entity types in dependency order.</summary>
    string[] EntityTypes { get; }

    /// <summary>Validate credentials with a test call. An exception's message is shown.</summary>
    Task<bool> TestConnectionAsync(string credentials, CancellationToken ct);

    /// <summary>One batch of records; a null cursor means the type is exhausted.</summary>
    Task<ImportBatch> ExtractAsync(string entityType, string credentials, string? cursor, CancellationToken ct);

    /// <summary>Human-readable name, e.g. "Zendesk".</summary>
    string DisplayName => Name;

    /// <summary>The credential form: name, label, type (text, password or url) and help.</summary>
    IReadOnlyList<IReadOnlyDictionary<string, object?>> CredentialFields => Array.Empty<IReadOnlyDictionary<string, object?>>();

    /// <summary>Default mappings for an entity type: source field to Escalated field.</summary>
    IReadOnlyDictionary<string, string> DefaultFieldMappings(string entityType) => new Dictionary<string, string>();

    /// <summary>Source fields as the Mapping screen lists them: name, label and escalated_options.</summary>
    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> AvailableSourceFieldsAsync(string entityType, string credentials, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(Array.Empty<IReadOnlyDictionary<string, object?>>());
}

public class ImportBatch
{
    public List<Dictionary<string, object?>> Records { get; set; } = new();
    public string? Cursor { get; set; }
    public int? TotalCount { get; set; }
    public bool IsExhausted => Cursor == null;
}

/// <summary>
/// Import mode, as escalated-laravel's <c>ImportContext</c>: while an import runs,
/// SLA attachment and breach and warning checks, escalation rules and automations
/// stand down, so imported history does not fire as if it were new. The flag is an
/// <see cref="AsyncLocal{T}"/>, so only the import's own flow is in it.
/// </summary>
public static class ImportContext
{
    private static readonly AsyncLocal<bool> Flag = new();

    public static bool IsImporting => Flag.Value;

    public static async Task<T> SuppressAsync<T>(Func<Task<T>> work)
    {
        var previous = Flag.Value;
        Flag.Value = true;
        try
        {
            return await work();
        }
        finally
        {
            Flag.Value = previous;
        }
    }

    public static Task SuppressAsync(Func<Task> work) => SuppressAsync(async () =>
    {
        await work();
        return true;
    });
}

/// <summary>
/// Seals a source's credentials (ASP.NET Data Protection) for where they rest: the
/// wizard's cookie, from a tested connection until the import starts, and the
/// import job's <c>Credentials</c> column until the run ends or is cancelled.
/// </summary>
public sealed class ImportCredentials
{
    private readonly IDataProtector _protector;

    public ImportCredentials(IDataProtectionProvider provider) =>
        _protector = provider.CreateProtector("Escalated.ImportCredentials");

    public string Seal(IReadOnlyDictionary<string, string> credentials) =>
        _protector.Protect(JsonSerializer.Serialize(credentials));

    /// <summary>The credentials, or null when absent, tampered with or sealed under another key.</summary>
    public Dictionary<string, string>? Open(string? token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(_protector.Unprotect(token));
        }
        catch (Exception e) when (e is System.Security.Cryptography.CryptographicException or JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// Runs an import outside the request that started it, as escalated-laravel queues
/// <c>ImportService::run</c>: a task in this process with its own DI scope. A host
/// with a job queue replaces <see cref="IImportDispatcher"/> and calls
/// <see cref="ImportService.RunAsync(Guid, CancellationToken)"/> from its worker.
/// </summary>
public interface IImportDispatcher
{
    void Dispatch(Guid jobId);
}

public sealed class BackgroundImportDispatcher : IImportDispatcher
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<BackgroundImportDispatcher> _logger;
    private readonly List<Task> _running = new();

    public BackgroundImportDispatcher(IServiceScopeFactory scopes, ILogger<BackgroundImportDispatcher> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    public void Dispatch(Guid jobId)
    {
        var task = Task.Run(async () =>
        {
            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ImportService>().RunAsync(jobId);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Import {JobId} failed", jobId);
            }
        });
        lock (_running) _running.Add(task);
    }

    /// <summary>Waits for every run started so far (tests, graceful shutdown).</summary>
    public async Task WaitAllAsync()
    {
        Task[] running;
        lock (_running) running = _running.ToArray();
        await Task.WhenAll(running);
    }
}

/// <summary>
/// Keeps import jobs and runs them, as escalated-laravel's <c>ImportService</c>:
/// walk the adapter's entity types in order under <see cref="ImportContext"/>,
/// resume each from its saved cursor and counters, write every record not yet in
/// the source map, log failures, save progress after each batch, and stop between
/// batches when the job has been paused or cancelled. A completed job drops its
/// credentials.
///
/// <para>Contacts the host has as users (<see cref="IUserDirectory"/>) import as
/// those users; others become Escalated contacts on guest tickets. Agents must
/// exist.</para>
/// </summary>
public class ImportService
{
    private const int MaxErrors = 10000;
    private const string ContactPrefix = "contact:";

    private readonly EscalatedDbContext _db;
    private readonly ILogger<ImportService> _logger;
    private readonly IEnumerable<IImportAdapter> _adapters;
    private readonly ImportCredentials _credentials;
    private readonly IUserDirectory? _users;

    public ImportService(EscalatedDbContext db, ILogger<ImportService> logger,
        IEnumerable<IImportAdapter> adapters, ImportCredentials credentials, IUserDirectory? users = null)
    {
        _db = db;
        _logger = logger;
        _adapters = adapters;
        _credentials = credentials;
        _users = users;
    }

    public IReadOnlyList<IImportAdapter> Adapters => _adapters.ToList();

    public IImportAdapter? ResolveAdapter(string platform) =>
        _adapters.FirstOrDefault(a => a.Name == platform);

    public ImportCredentials Credentials => _credentials;

    public async Task<bool> TestConnectionAsync(ImportJob job, CancellationToken ct = default)
    {
        var adapter = ResolveAdapter(job.Platform)
            ?? throw new InvalidOperationException($"No adapter found for platform '{job.Platform}'.");

        return await adapter.TestConnectionAsync(CredentialsJson(job), ct);
    }

    /// <summary>
    /// The job's credentials as the adapter takes them: sealed ones opened, and a
    /// plain JSON string (jobs created before credentials were sealed) as it is.
    /// </summary>
    public string CredentialsJson(ImportJob job)
    {
        if (string.IsNullOrEmpty(job.Credentials)) return "{}";
        var opened = _credentials.Open(job.Credentials);
        return opened is null ? job.Credentials : JsonSerializer.Serialize(opened);
    }

    /// <summary>Runs (or resumes) the job with this id. A run that throws marks it failed.</summary>
    public async Task RunAsync(Guid jobId, CancellationToken ct = default)
    {
        var job = await _db.ImportJobs.FindAsync(new object[] { jobId }, ct);
        if (job is null || job.Status == "completed") return;
        try
        {
            await RunAsync(job, null, ct);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Import {JobId} failed", jobId);
            await _db.ImportJobs.Where(j => j.Id == jobId)
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, "failed"), ct);
        }
    }

    /// <summary>Run (or resume) the import for a job.</summary>
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
        job.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        var credentials = CredentialsJson(job);
        var stopped = await ImportContext.SuppressAsync(async () =>
        {
            foreach (var entityType in adapter.EntityTypes)
            {
                if (await StoppedAsync(job, ct)) return true;
                if (await ImportEntityTypeAsync(job, adapter, entityType, credentials, onProgress, ct)) return true;
            }
            return false;
        });

        if (stopped || await StoppedAsync(job, ct)) return;

        job.Status = "completed";
        job.CompletedAt = DateTime.UtcNow;
        job.Credentials = null;
        job.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Re-reads the status the admin may have changed from another request: paused,
    /// or cancelled (failed). The run never writes the status back from its own copy.
    /// </summary>
    private async Task<bool> StoppedAsync(ImportJob job, CancellationToken ct)
    {
        var status = await _db.ImportJobs.AsNoTracking().Where(j => j.Id == job.Id).Select(j => j.Status).FirstOrDefaultAsync(ct);
        if (status is "paused" or "failed")
        {
            Settle(job, j => j.Status, status);
            return true;
        }
        return false;
    }

    /// <summary>Records a value the database already holds on the tracked job.</summary>
    private void Settle(ImportJob job, System.Linq.Expressions.Expression<Func<ImportJob, string?>> property, string value)
    {
        var entry = _db.Entry(job).Property(property);
        entry.CurrentValue = value;
        entry.OriginalValue = value;
    }

    public static Dictionary<string, Dictionary<string, object?>> ReadProgress(ImportJob job)
    {
        if (string.IsNullOrEmpty(job.Progress)) return new();
        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, JsonElement>>>(job.Progress) ?? new();
            return parsed.ToDictionary(p => p.Key, p => p.Value.ToDictionary(v => v.Key, v => (object?)(v.Value.ValueKind switch
            {
                JsonValueKind.Number => v.Value.GetInt32(),
                JsonValueKind.String => v.Value.GetString(),
                _ => null,
            })));
        }
        catch (JsonException)
        {
            return new();
        }
    }

    public static List<Dictionary<string, object?>> ReadErrors(ImportJob job)
    {
        if (string.IsNullOrEmpty(job.Errors)) return new();
        try
        {
            return JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(job.Errors) ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    /// <returns>True when the job was paused or cancelled mid-type.</returns>
    private async Task<bool> ImportEntityTypeAsync(ImportJob job, IImportAdapter adapter, string entityType,
        string credentials, Action<string, string>? onProgress, CancellationToken ct)
    {
        var progress = ReadProgress(job);
        var saved = progress.GetValueOrDefault(entityType) ?? new Dictionary<string, object?>
        {
            ["total"] = 0,
            ["processed"] = 0,
            ["skipped"] = 0,
            ["failed"] = 0,
            ["cursor"] = null,
        };
        int Count(string key) => saved.GetValueOrDefault(key) is int n ? n : 0;
        int processed = Count("processed"), skipped = Count("skipped"), failed = Count("failed");
        var cursor = saved.GetValueOrDefault("cursor") as string;
        var errors = ReadErrors(job);

        while (true)
        {
            var batch = await adapter.ExtractAsync(entityType, credentials, cursor, ct);

            foreach (var record in batch.Records)
            {
                var sourceId = Text(record, "source_id");
                if (string.IsNullOrEmpty(sourceId))
                {
                    failed++;
                    continue;
                }

                if (await ResolveAsync(job.Id, entityType, sourceId, ct) is not null)
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
                        CreatedAt = DateTime.UtcNow,
                    });
                    await _db.SaveChangesAsync(ct);
                    processed++;
                }
                catch (Exception ex)
                {
                    failed++;
                    DiscardPendingChanges(job);
                    _logger.LogWarning("Import failed for {EntityType} {SourceId}: {Error}", entityType, sourceId, ex.Message);
                    if (errors.Count < MaxErrors)
                    {
                        errors.Add(new Dictionary<string, object?>
                        {
                            ["entity_type"] = entityType,
                            ["source_id"] = sourceId,
                            ["error"] = ex.Message,
                            ["timestamp"] = DateTime.UtcNow.ToString("o"),
                        });
                    }
                }
            }

            saved["processed"] = processed;
            saved["skipped"] = skipped;
            saved["failed"] = failed;
            if (batch.TotalCount is { } total) saved["total"] = total;
            // As Laravel's: an exhausted extract keeps the last cursor, so a resume
            // re-reads one page, not the whole type.
            if (batch.Cursor is not null) saved["cursor"] = batch.Cursor;
            progress[entityType] = saved;

            var progressJson = JsonSerializer.Serialize(progress);
            var errorsJson = JsonSerializer.Serialize(errors);
            var now = DateTime.UtcNow;
            await _db.ImportJobs.Where(j => j.Id == job.Id).ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Progress, progressJson)
                .SetProperty(j => j.Errors, errorsJson)
                .SetProperty(j => j.UpdatedAt, now), ct);
            // Written already: the tracked copy takes the values as stored, so a
            // later SaveChanges does not write them again.
            Settle(job, j => j.Progress, progressJson);
            Settle(job, j => j.Errors, errorsJson);

            onProgress?.Invoke(entityType, $"processed={processed}, skipped={skipped}, failed={failed}");

            if (await StoppedAsync(job, ct)) return true;
            if (batch.Cursor is null) return false;
            cursor = batch.Cursor;
        }
    }

    private void DiscardPendingChanges(ImportJob job)
    {
        foreach (var entry in _db.ChangeTracker.Entries().Where(e => e.Entity != job && e.State is EntityState.Added or EntityState.Modified).ToList())
        {
            entry.State = entry.State == EntityState.Added ? EntityState.Detached : EntityState.Unchanged;
        }
    }

    private Task<string?> ResolveAsync(Guid jobId, string entityType, string sourceId, CancellationToken ct) =>
        _db.ImportSourceMaps.AsNoTracking()
            .Where(m => m.ImportJobId == jobId && m.EntityType == entityType && m.SourceId == sourceId)
            .Select(m => m.EscalatedId)
            .FirstOrDefaultAsync(ct);

    private async Task<string?> RefAsync(ImportJob job, string entityType, object? sourceId, CancellationToken ct)
    {
        var id = sourceId switch
        {
            null => null,
            JsonElement { ValueKind: JsonValueKind.String } e => e.GetString(),
            JsonElement { ValueKind: JsonValueKind.Number } e => e.GetRawText(),
            _ => sourceId.ToString(),
        };
        return string.IsNullOrEmpty(id) ? null : await ResolveAsync(job.Id, entityType, id, ct);
    }

    private static string? Text(Dictionary<string, object?> record, string key) =>
        record.GetValueOrDefault(key) switch
        {
            null => null,
            JsonElement { ValueKind: JsonValueKind.String } e => e.GetString(),
            JsonElement { ValueKind: JsonValueKind.Null } => null,
            JsonElement e => e.GetRawText(),
            var other => other.ToString(),
        } is { } s && s.Trim() != "" ? s.Trim() : null;

    private static bool Flag(Dictionary<string, object?> record, string key) =>
        record.GetValueOrDefault(key) switch
        {
            bool b => b,
            JsonElement { ValueKind: JsonValueKind.True } => true,
            var other => other?.ToString()?.ToLowerInvariant() is "true" or "1",
        };

    private async Task<string> PersistRecordAsync(ImportJob job, string entityType,
        Dictionary<string, object?> record, CancellationToken ct)
    {
        return entityType switch
        {
            "agents" => await PersistAgentAsync(record, ct),
            "contacts" => await PersistContactAsync(record, ct),
            "tags" => await PersistTagAsync(record, ct),
            "departments" => await PersistDepartmentAsync(record, ct),
            "tickets" => await PersistTicketAsync(job, record, ct),
            "replies" => await PersistReplyAsync(job, record, ct),
            _ => throw new InvalidOperationException($"Unknown entity type: {entityType}")
        };
    }

    private async Task<string?> HostUserIdAsync(string? email, CancellationToken ct)
    {
        if (_users is null || string.IsNullOrEmpty(email)) return null;
        var page = await _users.ListAsync(email, 1, 25, ct);
        return page.Items.FirstOrDefault(u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    private async Task<string> PersistAgentAsync(Dictionary<string, object?> record, CancellationToken ct)
    {
        var email = Text(record, "email");
        return await HostUserIdAsync(email, ct)
            ?? throw new InvalidOperationException($"Agent with email '{email}' not found in host application.");
    }

    private async Task<string> PersistContactAsync(Dictionary<string, object?> record, CancellationToken ct)
    {
        var email = Text(record, "email") ?? throw new InvalidOperationException("Contact has no email.");
        if (await HostUserIdAsync(email, ct) is { } userId) return userId;

        var normalized = email.Trim().ToLowerInvariant();
        var contact = await _db.Contacts.FirstOrDefaultAsync(c => c.Email == normalized, ct);
        if (contact is null)
        {
            contact = new Contact { Email = normalized, Name = Text(record, "name") };
            _db.Contacts.Add(contact);
            await _db.SaveChangesAsync(ct);
        }
        return ContactPrefix + contact.Id;
    }

    private async Task<string> PersistTagAsync(Dictionary<string, object?> record, CancellationToken ct)
    {
        var name = Text(record, "name") ?? "Unnamed";
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
        var name = Text(record, "name") ?? "Unnamed";
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

    private static readonly Dictionary<string, TicketStatus> Statuses =
        Enum.GetValues<TicketStatus>().ToDictionary(s => s.ToValue());

    private static readonly Dictionary<string, TicketPriority> Priorities =
        Enum.GetValues<TicketPriority>().ToDictionary(p => p.ToValue());

    private static DateTime? Timestamp(Dictionary<string, object?> record, string key) =>
        DateTime.TryParse(Text(record, key), System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var at)
            ? at
            : null;

    private async Task<string> PersistTicketAsync(ImportJob job, Dictionary<string, object?> record, CancellationToken ct)
    {
        var subject = Text(record, "title") ?? Text(record, "subject") ?? "Imported ticket";
        var ticket = new Ticket
        {
            Subject = subject,
            Description = Text(record, "description") ?? Text(record, "body") ?? subject,
            // As Laravel's TicketStatus::tryFrom / TicketPriority::tryFrom.
            Status = Statuses.GetValueOrDefault(Text(record, "status") ?? "", TicketStatus.Open),
            Priority = Priorities.GetValueOrDefault(Text(record, "priority") ?? "", TicketPriority.Medium),
            Reference = $"TEMP-{Guid.NewGuid():N}"[..32],
            CreatedAt = Timestamp(record, "created_at") ?? DateTime.UtcNow,
            UpdatedAt = Timestamp(record, "updated_at") ?? DateTime.UtcNow,
        };

        if (await RefAsync(job, "contacts", record.GetValueOrDefault("requester_source_id"), ct) is { } requester)
        {
            if (requester.StartsWith(ContactPrefix, StringComparison.Ordinal))
            {
                var contact = await _db.Contacts.FindAsync(new object[] { int.Parse(requester[ContactPrefix.Length..]) }, ct);
                if (contact is not null)
                {
                    ticket.ContactId = contact.Id;
                    ticket.GuestEmail = contact.Email;
                    ticket.GuestName = contact.Name;
                }
            }
            else
            {
                ticket.RequesterType = "User";
                ticket.RequesterId = requester;
            }
        }
        ticket.AssignedTo = await RefAsync(job, "agents", record.GetValueOrDefault("assignee_source_id"), ct);
        if (await RefAsync(job, "departments", record.GetValueOrDefault("department_source_id"), ct) is { } dept)
        {
            ticket.DepartmentId = int.Parse(dept);
        }

        _db.Tickets.Add(ticket);
        await _db.SaveChangesAsync(ct);
        ticket.Reference = ticket.GenerateReference("ESC");

        var tagSources = record.GetValueOrDefault("tag_source_ids") switch
        {
            JsonElement { ValueKind: JsonValueKind.Array } e => e.EnumerateArray().Select(x => (object?)x).ToList(),
            IEnumerable<object?> list => list.ToList(),
            System.Collections.IEnumerable list and not string => list.Cast<object?>().ToList(),
            _ => new List<object?>(),
        };
        foreach (var source in tagSources)
        {
            if (await RefAsync(job, "tags", source, ct) is { } tagId)
            {
                _db.TicketTags.Add(new TicketTag { TicketId = ticket.Id, TagId = int.Parse(tagId) });
            }
        }
        await _db.SaveChangesAsync(ct);

        return ticket.Id.ToString();
    }

    private async Task<string> PersistReplyAsync(ImportJob job, Dictionary<string, object?> record, CancellationToken ct)
    {
        var ticketId = await RefAsync(job, "tickets", record.GetValueOrDefault("ticket_source_id"), ct)
            ?? throw new InvalidOperationException("Parent ticket not found for reply.");

        var reply = new Reply
        {
            TicketId = int.Parse(ticketId),
            Body = Text(record, "body") ?? "",
            IsInternalNote = Flag(record, "is_internal_note"),
            Type = Flag(record, "is_internal_note") ? "note" : "reply",
            CreatedAt = Timestamp(record, "created_at") ?? DateTime.UtcNow,
            UpdatedAt = Timestamp(record, "updated_at") ?? DateTime.UtcNow,
        };

        var author = await RefAsync(job, "agents", record.GetValueOrDefault("author_source_id"), ct)
            ?? await RefAsync(job, "contacts", record.GetValueOrDefault("author_source_id"), ct);
        if (author is not null && !author.StartsWith(ContactPrefix, StringComparison.Ordinal))
        {
            reply.AuthorType = "User";
            reply.AuthorId = author;
        }

        _db.Replies.Add(reply);
        await _db.SaveChangesAsync(ct);
        return reply.Id.ToString();
    }
}
