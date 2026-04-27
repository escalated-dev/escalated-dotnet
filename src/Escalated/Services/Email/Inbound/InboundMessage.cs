namespace Escalated.Services.Email.Inbound;

/// <summary>
/// Transport-agnostic representation of an inbound email, independent
/// of the source adapter (Postmark, Mailgun, SES, IMAP, etc.).
///
/// Adapters normalize their webhook payload into this shape; the
/// <see cref="InboundEmailService"/> then routes it to a ticket by
/// parsing canonical Message-IDs out of <see cref="InReplyTo"/> /
/// <see cref="References"/> + verifying the signed Reply-To on
/// <see cref="ToEmail"/>.
/// </summary>
public sealed record InboundMessage
{
    /// <summary>Required — the sender's email address.</summary>
    public required string FromEmail { get; init; }

    public string? FromName { get; init; }

    /// <summary>
    /// Required — the recipient address we received this at. For a
    /// reply to a ticket notification, this is the signed Reply-To
    /// we set on outbound (<c>reply+{id}.{hmac8}@{domain}</c>).
    /// </summary>
    public required string ToEmail { get; init; }

    public required string Subject { get; init; }

    public string? BodyText { get; init; }
    public string? BodyHtml { get; init; }

    /// <summary>The Message-ID header on this inbound message.</summary>
    public string? MessageId { get; init; }

    /// <summary>The In-Reply-To header, parsed out of the payload.</summary>
    public string? InReplyTo { get; init; }

    /// <summary>
    /// The References header. May contain multiple Message-IDs
    /// separated by whitespace.
    /// </summary>
    public string? References { get; init; }

    public IReadOnlyDictionary<string, string> Headers { get; init; } =
        new Dictionary<string, string>();

    public IReadOnlyList<InboundAttachment> Attachments { get; init; } =
        Array.Empty<InboundAttachment>();

    /// <summary>
    /// Best body content (plain text preferred, HTML fallback).
    /// </summary>
    public string Body => BodyText ?? BodyHtml ?? string.Empty;
}

/// <summary>
/// Inbound attachment representation. Adapters either supply the
/// content inline (small attachments) or a URL (larger attachments
/// the adapter hosts).
/// </summary>
public sealed record InboundAttachment
{
    public required string Name { get; init; }
    public required string ContentType { get; init; }
    public long? SizeBytes { get; init; }

    /// <summary>Inline content (when size fits in the webhook payload).</summary>
    public byte[]? Content { get; init; }

    /// <summary>URL to download content from (Postmark / Mailgun-hosted).</summary>
    public string? DownloadUrl { get; init; }
}
