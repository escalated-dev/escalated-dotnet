using System.Collections.Concurrent;

namespace Escalated.Ui;

/// <summary>
/// Who is looking at a ticket right now, for the ticket page's presence indicator
/// (escalated-laravel keeps the same thing in its cache). The page polls; a viewer
/// who stops polling drops off after <see cref="Window"/>. In-process: behind several
/// instances each one knows only the viewers it served.
/// </summary>
public sealed class UiPresence
{
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<int, ConcurrentDictionary<string, Viewer>> _tickets = new();
    private readonly TimeProvider _clock;

    public UiPresence(TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
    }

    public sealed record Viewer(string Id, string Name, DateTimeOffset SeenAt);

    /// <summary>Records <paramref name="userId"/> as viewing the ticket and returns everyone else still viewing it.</summary>
    public IReadOnlyList<Viewer> Touch(int ticketId, string userId, string name)
    {
        var now = _clock.GetUtcNow();
        var viewers = _tickets.GetOrAdd(ticketId, _ => new ConcurrentDictionary<string, Viewer>());
        viewers[userId] = new Viewer(userId, name, now);

        var others = new List<Viewer>();
        foreach (var (id, viewer) in viewers)
        {
            if (now - viewer.SeenAt > Window)
            {
                viewers.TryRemove(id, out _);
            }
            else if (id != userId)
            {
                others.Add(viewer);
            }
        }

        return others.OrderBy(v => v.Name, StringComparer.Ordinal).ToList();
    }
}
