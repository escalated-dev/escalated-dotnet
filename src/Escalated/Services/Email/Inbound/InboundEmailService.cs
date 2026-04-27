using Escalated.Data;
using Escalated.Models;
using Microsoft.Extensions.Logging;

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
/// <para>Attachment persistence is scoped out: provider-hosted
/// attachments (Mailgun) carry their <c>DownloadUrl</c> through to
/// <see cref="ProcessResult"/> so a follow-up worker can fetch +
/// persist out-of-band.</para>
/// </summary>
public class InboundEmailService
{
    private readonly EscalatedDbContext _db;
    private readonly TicketService _tickets;
    private readonly InboundEmailRouter _router;
    private readonly ILogger<InboundEmailService> _logger;

    public InboundEmailService(
        EscalatedDbContext db,
        TicketService tickets,
        InboundEmailRouter router,
        ILogger<InboundEmailService> logger)
    {
        _db = db;
        _tickets = tickets;
        _router = router;
        _logger = logger;
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

        if (ticket is not null)
        {
            var reply = await _tickets.AddReplyAsync(
                ticket,
                body: message.Body,
                authorType: "inbound_email",
                isNote: false,
                ct: ct);

            inboundEmail.TicketId = ticket.Id;
            inboundEmail.ReplyId = reply.Id;
            inboundEmail.Status = "replied";
            inboundEmail.ProcessedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            return new ProcessResult
            {
                Outcome = ProcessOutcome.RepliedToExisting,
                TicketId = ticket.Id,
                ReplyId = reply.Id,
                PendingAttachmentDownloads = PendingDownloads(message),
            };
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

        return new ProcessResult
        {
            Outcome = ProcessOutcome.CreatedNew,
            TicketId = newTicket.Id,
            PendingAttachmentDownloads = PendingDownloads(message),
        };
    }

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

    private static IReadOnlyList<PendingAttachment> PendingDownloads(InboundMessage message)
    {
        var list = new List<PendingAttachment>();
        foreach (var attachment in message.Attachments)
        {
            if (!string.IsNullOrEmpty(attachment.DownloadUrl) && attachment.Content is null)
            {
                list.Add(new PendingAttachment
                {
                    Name = attachment.Name,
                    ContentType = attachment.ContentType,
                    SizeBytes = attachment.SizeBytes,
                    DownloadUrl = attachment.DownloadUrl,
                });
            }
        }
        return list;
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
