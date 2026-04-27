using System.Net.Http.Headers;
using System.Text;
using Escalated.Data;
using Escalated.Models;
using Microsoft.Extensions.Logging;

namespace Escalated.Services.Email.Inbound;

/// <summary>
/// Fetches provider-hosted attachments surfaced by
/// <see cref="ProcessResult.PendingAttachmentDownloads"/> and
/// persists them as <see cref="Attachment"/> rows tied to a ticket
/// (and optionally a reply).
///
/// <para>Mailgun hosts larger attachments behind a URL instead of
/// inlining them in the webhook payload; host apps run this in a
/// background worker after <see cref="InboundEmailService.ProcessAsync"/>
/// returns, so the webhook response can go back to the provider
/// immediately regardless of download latency.</para>
///
/// <para>Host apps with durable cloud storage needs (S3, Azure Blob,
/// etc.) can implement <see cref="IAttachmentStorage"/> themselves
/// and pass it to <see cref="AttachmentDownloader"/> instead of the
/// reference <see cref="LocalFileAttachmentStorage"/>.</para>
/// </summary>
public class AttachmentDownloader
{
    private readonly HttpClient _http;
    private readonly IAttachmentStorage _storage;
    private readonly EscalatedDbContext _db;
    private readonly ILogger<AttachmentDownloader> _logger;
    private readonly AttachmentDownloaderOptions _options;

    public AttachmentDownloader(
        HttpClient http,
        IAttachmentStorage storage,
        EscalatedDbContext db,
        ILogger<AttachmentDownloader> logger,
        AttachmentDownloaderOptions? options = null)
    {
        _http = http;
        _storage = storage;
        _db = db;
        _logger = logger;
        _options = options ?? new AttachmentDownloaderOptions();
    }

    /// <summary>
    /// Download one <see cref="PendingAttachment"/> and persist it as
    /// an <see cref="Attachment"/> row tied to the ticket (and
    /// optionally a reply).
    /// </summary>
    public async Task<Attachment> DownloadAsync(
        PendingAttachment pending,
        int ticketId,
        int? replyId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(pending.DownloadUrl))
        {
            throw new ArgumentException("Pending attachment has no download URL.", nameof(pending));
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, pending.DownloadUrl);
        if (_options.BasicAuth is { } auth)
        {
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{auth.Username}:{auth.Password}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        }

        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Attachment download failed: {pending.DownloadUrl} → HTTP {(int)response.StatusCode}");
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        if (_options.MaxBytes > 0 && bytes.LongLength > _options.MaxBytes)
        {
            throw new AttachmentTooLargeException(pending.Name, bytes.LongLength, _options.MaxBytes);
        }

        var contentType = !string.IsNullOrEmpty(pending.ContentType)
            ? pending.ContentType
            : response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";

        var filename = SafeFilename(pending.Name);

        using var stream = new MemoryStream(bytes);
        var path = await _storage.PutAsync(filename, stream, contentType, ct);

        var attachment = new Attachment
        {
            AttachableType = replyId.HasValue ? "reply" : "ticket",
            AttachableId = replyId ?? ticketId,
            Filename = filename,
            MimeType = contentType,
            Size = bytes.LongLength,
            Path = path,
            Disk = _storage.Name,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        _db.Attachments.Add(attachment);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "[AttachmentDownloader] Persisted {Filename} ({Bytes} bytes) for ticket #{TicketId}",
            filename, bytes.LongLength, ticketId);

        return attachment;
    }

    /// <summary>
    /// Download a batch of <see cref="PendingAttachment"/> records.
    /// Continues past per-attachment failures so a single bad URL
    /// doesn't prevent the rest from persisting. Returns a result
    /// record per input describing success/failure.
    /// </summary>
    public async Task<IReadOnlyList<AttachmentDownloadResult>> DownloadAllAsync(
        IReadOnlyList<PendingAttachment> pending,
        int ticketId,
        int? replyId,
        CancellationToken ct = default)
    {
        var results = new List<AttachmentDownloadResult>(pending.Count);
        foreach (var p in pending)
        {
            try
            {
                var attachment = await DownloadAsync(p, ticketId, replyId, ct);
                results.Add(new AttachmentDownloadResult(p, attachment, null));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[AttachmentDownloader] Failed to download {Url}", p.DownloadUrl);
                results.Add(new AttachmentDownloadResult(p, null, ex));
            }
        }
        return results;
    }

    /// <summary>
    /// Strip path separators so a crafted attachment name like
    /// <c>../../etc/passwd</c> can't escape the storage root. Falls
    /// back to <c>"attachment"</c> when the original name is unusable.
    /// </summary>
    public static string SafeFilename(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "attachment";
        }
        var cleaned = Path.GetFileName(name.Replace('\\', '/').Trim());
        if (string.IsNullOrEmpty(cleaned) || cleaned == "." || cleaned == "..")
        {
            return "attachment";
        }
        return cleaned;
    }
}

/// <summary>
/// Runtime configuration for <see cref="AttachmentDownloader"/>.
/// </summary>
public class AttachmentDownloaderOptions
{
    /// <summary>
    /// Reject attachments larger than this size. Zero disables
    /// the check.
    /// </summary>
    public long MaxBytes { get; set; } = 0;

    /// <summary>
    /// Optional HTTP basic auth credentials attached to every
    /// download request. Typical use: <c>("api", mailgunApiKey)</c>.
    /// </summary>
    public BasicAuth? BasicAuth { get; set; }
}

public record BasicAuth(string Username, string Password);

/// <summary>
/// Per-attachment outcome returned by
/// <see cref="AttachmentDownloader.DownloadAllAsync"/>.
/// <see cref="Persisted"/> is non-null on success;
/// <see cref="Error"/> is non-null on failure.
/// </summary>
public record AttachmentDownloadResult(
    PendingAttachment Pending,
    Attachment? Persisted,
    Exception? Error)
{
    public bool Succeeded => Persisted is not null;
}

/// <summary>
/// Thrown when a downloaded attachment exceeds
/// <see cref="AttachmentDownloaderOptions.MaxBytes"/>. The partial
/// body is not persisted.
/// </summary>
public class AttachmentTooLargeException : Exception
{
    public string AttachmentName { get; }
    public long ActualBytes { get; }
    public long MaxBytes { get; }

    public AttachmentTooLargeException(string name, long actual, long max)
        : base($"Attachment '{name}' is {actual} bytes, exceeds limit {max}.")
    {
        AttachmentName = name;
        ActualBytes = actual;
        MaxBytes = max;
    }
}

/// <summary>
/// Minimal storage contract implemented by
/// <see cref="LocalFileAttachmentStorage"/> and any host-provided
/// S3/Azure/GCS adapter.
/// </summary>
public interface IAttachmentStorage
{
    /// <summary>
    /// Name of the storage backend, written to
    /// <see cref="Attachment.Disk"/>.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Persist the given stream and return a storage-specific path
    /// or key that can later be used to retrieve it.
    /// </summary>
    Task<string> PutAsync(string filename, Stream content, string contentType, CancellationToken ct = default);
}
