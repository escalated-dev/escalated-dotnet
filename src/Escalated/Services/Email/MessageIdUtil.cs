using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Escalated.Services.Email;

/// <summary>
/// Pure helpers for RFC 5322 Message-ID threading and signed Reply-To
/// addresses. Mirrors the NestJS reference
/// <c>escalated-nestjs/src/services/email/message-id.ts</c> and the
/// Spring <c>dev.escalated.services.email.MessageIdUtil</c>.
///
/// <para>Message-ID format:</para>
/// <list type="bullet">
///   <item><c>&lt;ticket-{ticketId}@{domain}&gt;</c> — initial ticket email</item>
///   <item><c>&lt;ticket-{ticketId}-reply-{replyId}@{domain}&gt;</c> — agent reply</item>
/// </list>
///
/// <para>Signed Reply-To format:
/// <c>reply+{ticketId}.{hmac8}@{domain}</c></para>
///
/// <para>The signed Reply-To carries ticket identity even when clients
/// strip our Message-ID / In-Reply-To headers — the inbound provider
/// webhook verifies the 8-char HMAC-SHA256 prefix before routing.</para>
/// </summary>
public static class MessageIdUtil
{
    private static readonly Regex TicketIdPattern =
        new(@"ticket-(\d+)(?:-reply-\d+)?@", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ReplyLocalPattern =
        new(@"^reply\+(\d+)\.([a-f0-9]{8})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Build an RFC 5322 Message-ID. Pass <c>null</c> for <paramref name="replyId"/>
    /// on the initial ticket email; the <c>-reply-{id}</c> tail is
    /// appended only when <paramref name="replyId"/> is non-null.
    /// </summary>
    public static string BuildMessageId(long ticketId, long? replyId, string domain)
    {
        var body = replyId.HasValue
            ? $"ticket-{ticketId}-reply-{replyId.Value}"
            : $"ticket-{ticketId}";
        return $"<{body}@{domain}>";
    }

    /// <summary>
    /// Extract the ticket id from a Message-ID we issued. Accepts the
    /// header value with or without angle brackets. Returns <c>null</c>
    /// when the input doesn't match our shape.
    /// </summary>
    public static long? ParseTicketIdFromMessageId(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        var match = TicketIdPattern.Match(raw);
        if (!match.Success) return null;
        return long.TryParse(match.Groups[1].Value, out var id) ? id : null;
    }

    /// <summary>
    /// Build a signed Reply-To address.
    /// </summary>
    public static string BuildReplyTo(long ticketId, string secret, string domain)
        => $"reply+{ticketId}.{Sign(ticketId, secret)}@{domain}";

    /// <summary>
    /// Verify a reply-to address (full <c>local@domain</c> or just the
    /// local part). Returns the ticket id on match, <c>null</c> otherwise.
    /// </summary>
    public static long? VerifyReplyTo(string? address, string secret)
    {
        if (string.IsNullOrEmpty(address)) return null;
        var at = address.IndexOf('@');
        var local = at > 0 ? address[..at] : address;
        var match = ReplyLocalPattern.Match(local);
        if (!match.Success) return null;
        if (!long.TryParse(match.Groups[1].Value, out var ticketId)) return null;
        var expected = Sign(ticketId, secret);
        return CryptographicOperations.FixedTimeEquals(
                   Encoding.ASCII.GetBytes(expected.ToLowerInvariant()),
                   Encoding.ASCII.GetBytes(match.Groups[2].Value.ToLowerInvariant()))
            ? ticketId
            : null;
    }

    private static string Sign(long ticketId, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var digest = hmac.ComputeHash(Encoding.UTF8.GetBytes(ticketId.ToString()));
        return Convert.ToHexString(digest, 0, 4).ToLowerInvariant();
    }
}
