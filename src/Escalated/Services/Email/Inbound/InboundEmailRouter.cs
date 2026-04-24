using Escalated.Configuration;
using Escalated.Data;
using Escalated.Models;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Services.Email.Inbound;

/// <summary>
/// Resolves an inbound email to an existing ticket via canonical
/// Message-ID parsing + signed Reply-To verification.
///
/// <para>Resolution order (first match wins):</para>
/// <list type="number">
///   <item>
///     <c>In-Reply-To</c> parsed via <see cref="MessageIdUtil.ParseTicketIdFromMessageId"/>
///     — cold-start path, no DB lookup on the header required (we know
///     our own Message-ID format).
///   </item>
///   <item>
///     <c>References</c> parsed via <see cref="MessageIdUtil.ParseTicketIdFromMessageId"/>,
///     each id in order.
///   </item>
///   <item>
///     Signed <c>Reply-To</c> on <see cref="InboundMessage.ToEmail"/>
///     (<c>reply+{id}.{hmac8}@...</c>) verified via
///     <see cref="MessageIdUtil.VerifyReplyTo"/>. Survives clients that
///     strip our threading headers; forged signatures are rejected.
///   </item>
///   <item>
///     Subject line reference tag (e.g. <c>[ESC-00001]</c>) — legacy.
///   </item>
///   <item>
///     <see cref="InboundEmail.MessageId"/> lookup in the audit log —
///     weakest fallback, covers pre-migration message IDs we sent
///     before the canonical format was in place.
///   </item>
/// </list>
///
/// <para>Mirrors the NestJS <c>InboundRouterService</c> resolution
/// order and the Laravel/Rails/Django/Adonis/WordPress ports.</para>
/// </summary>
public class InboundEmailRouter
{
    private readonly EscalatedDbContext _db;
    private readonly EscalatedOptions _options;

    public InboundEmailRouter(EscalatedDbContext db, EscalatedOptions options)
    {
        _db = db;
        _options = options;
    }

    /// <summary>
    /// Resolve the inbound email to an existing ticket, or <c>null</c>
    /// when no match (caller should create a new ticket).
    /// </summary>
    public async Task<Ticket?> ResolveTicketAsync(InboundMessage message, CancellationToken ct = default)
    {
        var headerIds = CandidateHeaderMessageIds(message).ToList();

        // 1 + 2. Parse canonical Message-IDs out of our own headers.
        foreach (var raw in headerIds)
        {
            var ticketId = MessageIdUtil.ParseTicketIdFromMessageId(raw);
            if (ticketId is null) continue;
            var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == (int)ticketId.Value, ct);
            if (ticket is not null) return ticket;
        }

        // 3. Signed Reply-To on the recipient address.
        var secret = _options.Email.InboundSecret;
        if (!string.IsNullOrEmpty(secret) && !string.IsNullOrEmpty(message.ToEmail))
        {
            var verified = MessageIdUtil.VerifyReplyTo(message.ToEmail, secret);
            if (verified is not null)
            {
                var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == (int)verified.Value, ct);
                if (ticket is not null) return ticket;
            }
        }

        // 4. Subject line reference tag — match the configured prefix.
        var prefix = _options.TicketReferencePrefix;
        var subjectMatch = System.Text.RegularExpressions.Regex.Match(
            message.Subject ?? string.Empty,
            $@"\[({System.Text.RegularExpressions.Regex.Escape(prefix)}-\d+)\]");
        if (subjectMatch.Success)
        {
            var reference = subjectMatch.Groups[1].Value;
            var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Reference == reference, ct);
            if (ticket is not null) return ticket;
        }

        // 5. Legacy InboundEmail lookup — covers pre-migration Message-IDs.
        if (headerIds.Count > 0)
        {
            var related = await _db.Set<InboundEmail>()
                .Where(e => e.Status == "processed" && e.TicketId != null
                            && e.MessageId != null && headerIds.Contains(e.MessageId))
                .OrderByDescending(e => e.Id)
                .FirstOrDefaultAsync(ct);
            if (related?.TicketId is not null)
            {
                return await _db.Tickets.FirstOrDefaultAsync(t => t.Id == related.TicketId.Value, ct);
            }
        }

        return null;
    }

    /// <summary>
    /// Return every candidate Message-ID from the inbound headers in
    /// the order the mail client sent them. Caller iterates the
    /// result and stops at the first resolvable id.
    /// </summary>
    internal static IEnumerable<string> CandidateHeaderMessageIds(InboundMessage message)
    {
        if (!string.IsNullOrEmpty(message.InReplyTo))
        {
            yield return message.InReplyTo;
        }
        if (!string.IsNullOrEmpty(message.References))
        {
            foreach (var raw in message.References.Split(
                new[] { ' ', '\t', '\n', '\r' },
                StringSplitOptions.RemoveEmptyEntries))
            {
                yield return raw.Trim();
            }
        }
    }
}
