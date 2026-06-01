using System.Net;
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

        var downloadUri = ValidateDownloadUri(pending.DownloadUrl);

        using var request = new HttpRequestMessage(HttpMethod.Get, downloadUri);
        if (_options.BasicAuth is { } auth)
        {
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{auth.Username}:{auth.Password}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        }

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Attachment download failed: {pending.DownloadUrl} → HTTP {(int)response.StatusCode}");
        }

        if (_options.MaxBytes > 0 &&
            response.Content.Headers.ContentLength is { } contentLength &&
            contentLength > _options.MaxBytes)
        {
            throw new AttachmentTooLargeException(pending.Name, contentLength, _options.MaxBytes);
        }

        var bytes = await ReadContentWithinLimitAsync(response.Content, pending.Name, _options.MaxBytes, ct);

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

    private static Uri ValidateDownloadUri(string downloadUrl)
    {
        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("Attachment download URL must be absolute.");
        }

        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
        {
            throw new InvalidOperationException("Attachment download URL must use HTTP or HTTPS.");
        }

        if (IsLocalAddress(uri.Host))
        {
            throw new InvalidOperationException("Attachment download URL cannot target a local address.");
        }

        return uri;
    }

    private static bool IsLocalAddress(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("localhost.localdomain", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(host, out var address) && IsPrivateOrLocalAddress(address);
    }

    private static bool IsPrivateOrLocalAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) ||
            address.Equals(IPAddress.Any) ||
            address.Equals(IPAddress.IPv6Any))
        {
            return true;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return bytes[0] == 0 ||
                   bytes[0] == 10 ||
                   bytes[0] == 127 ||
                   bytes[0] == 169 && bytes[1] == 254 ||
                   bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31 ||
                   bytes[0] == 192 && bytes[1] == 168 ||
                   bytes[0] >= 224;
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal ||
                   address.IsIPv6Multicast ||
                   address.IsIPv6SiteLocal ||
                   bytes[0] == 0xfc ||
                   bytes[0] == 0xfd;
        }

        return false;
    }

    private static async Task<byte[]> ReadContentWithinLimitAsync(
        HttpContent content,
        string attachmentName,
        long maxBytes,
        CancellationToken ct)
    {
        await using var responseStream = await content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var readBuffer = new byte[81920];

        while (true)
        {
            var read = await responseStream.ReadAsync(readBuffer, ct);
            if (read == 0)
            {
                return buffer.ToArray();
            }

            if (maxBytes > 0 && buffer.Length + read > maxBytes)
            {
                throw new AttachmentTooLargeException(attachmentName, buffer.Length + read, maxBytes);
            }

            buffer.Write(readBuffer, 0, read);
        }
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
