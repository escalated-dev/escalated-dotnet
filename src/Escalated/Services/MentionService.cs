using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Escalated.Data;
using Escalated.Models;
using Escalated.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Services;

/// <summary>
/// Parses, resolves, persists and notifies @-mentions in internal notes.
///
/// Ports the Laravel reference <c>Escalated\Laravel\Services\MentionService</c>:
/// an internal note body is scanned for <c>@handle</c> tokens, each handle is
/// resolved against the host user directory (by display name or email
/// local-part), a <see cref="Mention"/> row is persisted per resolved agent
/// (skipping the author's own mention and de-duplicating), and each mentioned
/// agent is notified via <see cref="IEscalatedNotificationSender"/>.
///
/// The pure <see cref="ExtractMentions"/> / <see cref="ExtractUsernameFromEmail"/>
/// helpers keep the parameterless-constructor shape used by the frontend
/// autocomplete and by unit tests; the dependency-injected constructor adds
/// the resolve/persist/notify pipeline wired from <c>TicketService</c>.
/// </summary>
public class MentionService
{
    private static readonly Regex MentionRegex = new(@"@(\w+(?:\.\w+)*)", RegexOptions.Compiled);

    // Upper bound on directory rows scanned per handle when resolving.
    private const int ResolveLimit = 25;

    private readonly EscalatedDbContext? _db;
    private readonly IUserDirectory? _users;
    private readonly IEscalatedNotificationSender? _notifications;

    /// <summary>Pure-extraction constructor (frontend autocomplete / unit tests).</summary>
    public MentionService()
    {
    }

    /// <summary>Full pipeline constructor resolved by the DI container.</summary>
    public MentionService(EscalatedDbContext db, IUserDirectory users, IEscalatedNotificationSender notifications)
    {
        _db = db;
        _users = users;
        _notifications = notifications;
    }

    /// <summary>Extracts the distinct raw <c>@handle</c> tokens from a note body.</summary>
    public List<string> ExtractMentions(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new List<string>();
        var matches = MentionRegex.Matches(text);
        return matches.Select(m => m.Groups[1].Value).Distinct().ToList();
    }

    /// <summary>Returns the local-part of an email (the bit before <c>@</c>).</summary>
    public string ExtractUsernameFromEmail(string email)
    {
        if (string.IsNullOrEmpty(email)) return "";
        var parts = email.Split('@');
        return parts.Length > 0 ? parts[0] : email;
    }

    /// <summary>
    /// Resolves the raw handles in a note body to distinct host user ids, via
    /// the registered <see cref="IUserDirectory"/>. A handle matches a user
    /// whose display name equals the handle, or whose email (or its local-part)
    /// equals the handle — case-insensitively. Unknown handles are ignored.
    /// </summary>
    public async Task<List<string>> ResolveMentionsAsync(string body, CancellationToken ct = default)
    {
        if (_users is null) return new List<string>();

        var handles = ExtractMentions(body);
        if (handles.Count == 0) return new List<string>();

        var resolved = new List<string>();
        var seen = new HashSet<string>();

        foreach (var handle in handles)
        {
            var page = await _users.ListAsync(handle, 1, ResolveLimit, ct);
            foreach (var entry in page.Items)
            {
                if (MatchesHandle(entry, handle) && seen.Add(entry.Id))
                {
                    resolved.Add(entry.Id);
                }
            }
        }

        return resolved;
    }

    /// <summary>
    /// Parses, resolves, persists and notifies @-mentions found in the given
    /// note. The author is never mentioned to themselves and existing mentions
    /// for the (reply, user) pair are not duplicated.
    /// </summary>
    public async Task ProcessMentionsAsync(Reply reply, Ticket ticket, CancellationToken ct = default)
    {
        if (_db is null || _users is null) return;

        var userIds = await ResolveMentionsAsync(reply.Body, ct);
        if (userIds.Count == 0) return;

        foreach (var userId in userIds)
        {
            // Never notify the author about their own mention.
            if (reply.AuthorId != null && userId == reply.AuthorId) continue;

            var already = await _db.Mentions
                .AnyAsync(m => m.ReplyId == reply.Id && m.UserId == userId, ct);
            if (already) continue;

            _db.Mentions.Add(new Mention
            {
                ReplyId = reply.Id,
                UserId = userId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            await _db.SaveChangesAsync(ct);

            if (_notifications is not null)
            {
                await _notifications.SendMentionNotificationAsync(reply, ticket, userId, ct);
            }
        }
    }

    private bool MatchesHandle(UserDirectoryEntry entry, string handle)
    {
        if (entry.Name is not null && entry.Name.Equals(handle, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (entry.Email is not null)
        {
            if (entry.Email.Equals(handle, StringComparison.OrdinalIgnoreCase)) return true;
            if (ExtractUsernameFromEmail(entry.Email).Equals(handle, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }
}
