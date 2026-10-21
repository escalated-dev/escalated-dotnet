using Escalated.Data;
using Escalated.Enums;
using Escalated.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Escalated.Configuration;

namespace Escalated.Services.Email.Inbound;

/// <summary>
/// Orchestrates the full inbound email pipeline:
/// <c>parser output → router resolution → reply-on-existing or
/// create-new-ticket</c>.
///
/// <para>Called from <see cref="Controllers.InboundEmailController"/>
/// after the parser normalizes the provider payload and the router
/// decides whether this is a reply or a new ticket. Writes the
/// <see cref="InboundEmail"/> audit row transition and delegates to
/// <see cref="TicketService.AddReplyAsync"/> or
/// <see cref="TicketService.CreateAsync"/>.</para>
///
/// <para>A thread match alone is not enough to post: the sender must
/// be the ticket's requester (guest email, contact email, or the
/// requester user's email, compared case-insensitively), and the reply
/// is posted as that requester, never as an identity named by the
/// unauthenticated <c>From</c> header. Anything else becomes a new
/// ticket, and only an accepted reply reopens a resolved or closed
/// ticket.</para>
///
/// <para>Attachments are stored on the reply (or, for a new ticket, the
/// ticket): inline content through <see cref="IAttachmentStorage"/>,
/// provider-hosted files (Mailgun) through <see cref="AttachmentDownloader"/>.
/// Like Laravel, at most <c>Attachments.MaxPerReply</c> are kept, and files over
/// <c>Attachments.MaxSizeKb</c> or with executable/script extensions are skipped.
/// A hosted file that could not be fetched is returned in
/// <see cref="ProcessResult.PendingAttachmentDownloads"/> so the host can
/// retry it.</para>
/// </summary>
public class InboundEmailService
{
    private readonly EscalatedDbContext _db;
    private readonly TicketService _tickets;
    private readonly InboundEmailRouter _router;
    private readonly ILogger<InboundEmailService> _logger;
    private readonly IUserDirectory? _users;
    private readonly IAttachmentStorage? _storage;
    private readonly AttachmentDownloader? _downloader;
    private readonly AttachmentOptions _attachmentOptions;

    /// <summary>Never accepted from inbound email or page uploads (same list as Laravel).</summary>
    internal static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "exe", "bat", "cmd", "com", "msi", "scr", "pif", "vbs", "vbe",
        "js", "jse", "wsf", "wsh", "ps1", "psm1", "psd1", "reg",
        "cpl", "hta", "inf", "lnk", "sct", "shb", "sys", "drv",
        "php", "phtml", "php3", "php4", "php5", "phar",
        "sh", "bash", "csh", "ksh", "pl", "py", "rb",
        "dll", "so", "dylib",
    };

    public InboundEmailService(
        EscalatedDbContext db,
        TicketService tickets,
        InboundEmailRouter router,
        ILogger<InboundEmailService> logger,
        IUserDirectory? users = null,
        IAttachmentStorage? storage = null,
        AttachmentDownloader? downloader = null,
        IOptions<EscalatedOptions>? options = null)
    {
        _db = db;
        _tickets = tickets;
        _router = router;
        _logger = logger;
        _users = users;
        _storage = storage;
        _downloader = downloader;
        _attachmentOptions = options?.Value.Attachments ?? new AttachmentOptions();
    }

    /// <summary>
    /// Process a parsed inbound message against the existing audit
    /// row. Returns a <see cref="ProcessResult"/> carrying the
    /// outcome (matched + reply id, or new ticket id, or skipped).
    /// </summary>
    public async Task<ProcessResult> ProcessAsync(
        InboundMessage message,
        InboundEmail inboundEmail,
        CancellationToken ct = default)
    {
        var ticket = await _router.ResolveTicketAsync(message, ct);
        var author = ticket is null ? null : await ResolveReplyAuthorAsync(ticket, message, ct);

        if (ticket is not null && author is not null)
        {
            var reply = await _tickets.AddReplyAsync(
                ticket,
                body: message.Body,
                authorId: author.AuthorId,
                authorType: "inbound_email",
                isNote: false,
                ct: ct);

            if (ticket.Status is TicketStatus.Resolved or TicketStatus.Closed)
            {
                await _tickets.ChangeStatusAsync(ticket, TicketStatus.Reopened, author.AuthorId, ct);
            }

            inboundEmail.TicketId = ticket.Id;
            inboundEmail.ReplyId = reply.Id;
            inboundEmail.Status = "replied";
            inboundEmail.ProcessedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            var replyPending = await StoreAttachmentsAsync(message, ticket.Id, reply.Id, ct);

            return new ProcessResult
            {
                Outcome = ProcessOutcome.RepliedToExisting,
                TicketId = ticket.Id,
                ReplyId = reply.Id,
                PendingAttachmentDownloads = replyPending,
            };
        }

        if (ticket is not null)
        {
            _logger.LogInformation(
                "[InboundEmailService] Inbound email {InboundId} matched ticket #{TicketId} but not its requester; opening a new ticket",
                inboundEmail.Id, ticket.Id);
        }

        // Skip SNS subscription confirmations / bounce echoes — these
        // match on neither threading nor subject and have no human
        // content to create a ticket from.
        if (IsNoiseEmail(message))
        {
            inboundEmail.Status = "skipped";
            inboundEmail.ProcessedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            return new ProcessResult { Outcome = ProcessOutcome.Skipped };
        }

        var newTicket = await _tickets.CreateAsync(
            subject: string.IsNullOrWhiteSpace(message.Subject) ? "(no subject)" : message.Subject,
            description: message.Body,
            guestName: message.FromName,
            guestEmail: message.FromEmail,
            ct: ct);

        inboundEmail.TicketId = newTicket.Id;
        inboundEmail.Status = "created";
        inboundEmail.ProcessedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "[InboundEmailService] Created ticket #{TicketId} from inbound email {InboundId}",
            newTicket.Id, inboundEmail.Id);

        var ticketPending = await StoreAttachmentsAsync(message, newTicket.Id, null, ct);

        return new ProcessResult
        {
            Outcome = ProcessOutcome.CreatedNew,
            TicketId = newTicket.Id,
            PendingAttachmentDownloads = ticketPending,
        };
    }

    /// <summary>
    /// Decide who a threaded inbound email may post as. Returns the
    /// requester (a user id, or a null id for a guest reply) when the
    /// <c>From</c> address is the ticket's guest email, its contact's
    /// email, or its requester user's email; returns <c>null</c> when
    /// the sender is anyone else. Staff identity is never derived from
    /// <c>From</c>: an agent replying by email is not the requester, so
    /// the message becomes a new ticket instead.
    /// </summary>
    private async Task<ReplyAuthor?> ResolveReplyAuthorAsync(
        Ticket ticket, InboundMessage message, CancellationToken ct)
    {
        var sender = NormalizeEmail(message.FromEmail);
        if (sender.Length == 0) return null;

        if (NormalizeEmail(ticket.GuestEmail) == sender) return new ReplyAuthor(null);

        if (ticket.ContactId is not null)
        {
            var contactEmail = await _db.Contacts
                .Where(c => c.Id == ticket.ContactId.Value)
                .Select(c => c.Email)
                .FirstOrDefaultAsync(ct);
            if (NormalizeEmail(contactEmail) == sender) return new ReplyAuthor(null);
        }

        if (!string.IsNullOrEmpty(ticket.RequesterId) && _users is not null)
        {
            var requester = await _users.FindAsync(ticket.RequesterId, ct);
            if (requester is not null && NormalizeEmail(requester.Email) == sender)
            {
                return new ReplyAuthor(ticket.RequesterId);
            }
        }

        return null;
    }

    private static string NormalizeEmail(string? email)
        => (email ?? string.Empty).Trim().ToLowerInvariant();

    private sealed record ReplyAuthor(string? AuthorId);

    /// <summary>
    /// Noise emails: empty body + empty subject, or from common
    /// bounce/no-reply / SNS confirmation senders. Skip rather than
    /// create a new ticket.
    /// </summary>
    public static bool IsNoiseEmail(InboundMessage message)
    {
        if (string.Equals(
                message.FromEmail,
                "no-reply@sns.amazonaws.com",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (string.IsNullOrWhiteSpace(message.Body) && string.IsNullOrWhiteSpace(message.Subject))
        {
            return true;
        }
        return false;
    }

    /// <summary>
    /// Stores the message attachments against the reply (or the ticket when
    /// <paramref name="replyId"/> is null). Returns the provider-hosted ones
    /// that still need fetching: all of them when no downloader is available,
    /// otherwise only those whose download failed.
    /// </summary>
    private async Task<IReadOnlyList<PendingAttachment>> StoreAttachmentsAsync(
        InboundMessage message, int ticketId, int? replyId, CancellationToken ct)
    {
        var pending = new List<PendingAttachment>();
        if (message.Attachments.Count == 0) return pending;

        var maxBytes = (long)_attachmentOptions.MaxSizeKb * 1024;
        var maxCount = _attachmentOptions.MaxPerReply;
        var stored = 0;

        foreach (var attachment in message.Attachments)
        {
            if (maxCount > 0 && stored >= maxCount)
            {
                _logger.LogInformation("[InboundEmailService] Attachment limit {Max} reached; skipping the rest", maxCount);
                break;
            }

            var filename = AttachmentDownloader.SafeFilename(attachment.Name);
            var extension = Path.GetExtension(filename).TrimStart('.');
            if (BlockedExtensions.Contains(extension))
            {
                _logger.LogInformation("[InboundEmailService] Blocked inbound attachment {Filename}", filename);
                continue;
            }

            var size = attachment.Content?.LongLength ?? attachment.SizeBytes ?? 0;
            if (maxBytes > 0 && size > maxBytes)
            {
                _logger.LogInformation("[InboundEmailService] Skipped oversized inbound attachment {Filename} ({Bytes} bytes)", filename, size);
                continue;
            }

            if (attachment.Content is { } content)
            {
                if (_storage is null)
                {
                    _logger.LogWarning("[InboundEmailService] No IAttachmentStorage registered; dropping {Filename}", filename);
                    continue;
                }

                var contentType = string.IsNullOrWhiteSpace(attachment.ContentType) ? "application/octet-stream" : attachment.ContentType;
                using var stream = new MemoryStream(content, writable: false);
                var path = await _storage.PutAsync(filename, stream, contentType, ct);
                _db.Attachments.Add(new Attachment
                {
                    AttachableType = replyId.HasValue ? "reply" : "ticket",
                    AttachableId = replyId ?? ticketId,
                    Filename = filename,
                    MimeType = contentType,
                    Size = content.LongLength,
                    Path = path,
                    Disk = _storage.Name,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                });
                await _db.SaveChangesAsync(ct);
                stored++;
                continue;
            }

            if (string.IsNullOrEmpty(attachment.DownloadUrl)) continue;

            var hosted = new PendingAttachment
            {
                Name = attachment.Name,
                ContentType = attachment.ContentType,
                SizeBytes = attachment.SizeBytes,
                DownloadUrl = attachment.DownloadUrl,
            };

            if (_downloader is null)
            {
                pending.Add(hosted);
                continue;
            }

            try
            {
                await _downloader.DownloadAsync(hosted, ticketId, replyId, ct);
                stored++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "[InboundEmailService] Could not download inbound attachment {Filename}", filename);
                pending.Add(hosted);
            }
        }

        return pending;
    }
}

public enum ProcessOutcome
{
    RepliedToExisting,
    CreatedNew,
    Skipped,
}

public class ProcessResult
{
    public ProcessOutcome Outcome { get; init; }
    public int? TicketId { get; init; }
    public int? ReplyId { get; init; }

    /// <summary>
    /// Provider-hosted attachments the host app should download
    /// out-of-band (Mailgun hosts content behind a URL for large
    /// files). Empty when all attachments came inline.
    /// </summary>
    public IReadOnlyList<PendingAttachment> PendingAttachmentDownloads { get; init; }
        = Array.Empty<PendingAttachment>();
}

public class PendingAttachment
{
    public required string Name { get; init; }
    public required string ContentType { get; init; }
    public long? SizeBytes { get; init; }
    public required string DownloadUrl { get; init; }
}
