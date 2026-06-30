namespace Escalated.Services;

/// <summary>
/// Resolves the recipient user ids for a ticket's followers.
///
/// The package abstracts the host user table, so it cannot email follower
/// users itself — these ids are exposed for the host app to deliver to.
/// See issue #92.
/// </summary>
public static class FollowerRecipients
{
    /// <summary>
    /// Excludes the actor (a user is never notified of their own action) and
    /// de-duplicates the given user ids, preserving order.
    /// </summary>
    public static IReadOnlyList<string> Resolve(IEnumerable<string> userIds, string? excludeUserId)
    {
        var result = new List<string>();
        var seen = new HashSet<string>();

        foreach (var userId in userIds)
        {
            if (userId == excludeUserId || !seen.Add(userId))
            {
                continue;
            }

            result.Add(userId);
        }

        return result;
    }
}
