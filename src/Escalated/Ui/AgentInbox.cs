using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Escalated.Configuration;
using Escalated.Data;
using Escalated.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Escalated.Ui;

/// <summary>A queue the agent list shows: its key, title and own filters (what its counts and stats are over).</summary>
public sealed record AgentQueue(string Key, string Title, IReadOnlyDictionary<string, object?> Filters)
{
    /// <summary>The <c>queue</c> prop.</summary>
    public Dictionary<string, object?> Prop() => new() { ["key"] = Key, ["title"] = Title };
}

/// <summary>A view in the rail: its id, name and the list filters it links to.</summary>
public sealed record AgentInboxView(object Id, string Name, IReadOnlyDictionary<string, object?> Filters);

/// <summary>
/// The agent panel's optional <c>inbox</c> and <c>queue</c> props (shared frontend
/// <c>docs/agent-panel.md</c>), as escalated-laravel's <c>AgentInbox</c> builds them.
///
/// <para>Every number is a count over the same filters the ticket list applies
/// (<see cref="TicketListQuery"/>, <see cref="TicketService.CountAsync"/>), through
/// the ticket visibility filter, so it is what the agent would see by opening that
/// list. Rail counts (<c>counts</c>, <c>views</c>) are of unresolved, unsnoozed
/// tickets unless a view's filters name a status; <c>stats</c> are for the current
/// queue. They are kept per agent and queue for
/// <see cref="UiOptions.AgentInboxCacheSeconds"/>.</para>
/// </summary>
public sealed class AgentInbox
{
    /// <summary>The cookie that remembers the queue last shown in the list, for the ticket page.</summary>
    public const string QueueCookie = "escalated_agent_queue";

    /// <summary>Saved views beyond this many are not listed, so the counts stay bounded.</summary>
    public const int MaxSavedViews = 50;

    private static readonly string[] PresentationKeys = ["page", "per_page", "view"];

    private static readonly Dictionary<string, string> QueueTitles = new()
    {
        ["mine"] = "My inbox",
        ["unassigned"] = "Unassigned",
        ["all"] = "All tickets",
    };

    private readonly EscalatedDbContext _db;
    private readonly TicketService _tickets;
    private readonly AgentInboxCache _cache;
    private readonly EscalatedOptions _options;
    private List<AgentInboxView>? _views;

    public AgentInbox(EscalatedDbContext db, TicketService tickets, AgentInboxCache cache, IOptions<EscalatedOptions> options)
    {
        _db = db;
        _tickets = tickets;
        _cache = cache;
        _options = options.Value;
    }

    /// <summary>The list query with <c>assigned=me</c> and <c>assigned_to=me</c> as the agent's id.</summary>
    public static Dictionary<string, string?> Normalise(IReadOnlyDictionary<string, string?> query, string? agentId)
    {
        var output = new Dictionary<string, string?>(query, StringComparer.Ordinal);
        if (agentId is not null && (TicketListQuery.Get(output, "assigned") == "me" || TicketListQuery.Get(output, "assigned_to") == "me"))
        {
            output["assigned_to"] = agentId;
        }

        output.Remove("assigned");
        return output;
    }

    /// <summary>The queue a list request shows.</summary>
    public async Task<AgentQueue> QueueForAsync(IReadOnlyDictionary<string, string?> query, string? agentId, CancellationToken ct)
    {
        var views = await ViewsAsync(agentId, ct);
        if (TicketListQuery.Get(query, "view") is { } viewId
            && views.FirstOrDefault(v => Id(v.Id) == viewId) is { } picked)
        {
            return ViewQueue(picked);
        }

        var selection = Comparable(query.ToDictionary(q => q.Key, q => (object?)q.Value));
        if (selection.Count > 0
            && views.FirstOrDefault(v => v.Filters.Count > 0 && Same(Comparable(v.Filters), selection)) is { } matched)
        {
            return ViewQueue(matched);
        }

        if (agentId is not null && TicketListQuery.Get(query, "assigned_to") == agentId)
        {
            return WorkspaceQueue("mine", agentId);
        }

        return WorkspaceQueue(TicketListQuery.Truthy(query, "unassigned") ? "unassigned" : "all", agentId);
    }

    /// <summary>The query the list applies: the request's, over a view picked by id.</summary>
    public static IReadOnlyDictionary<string, string?> ListQuery(Dictionary<string, string?> query, AgentQueue queue)
    {
        if (TicketListQuery.Get(query, "view") is null || !queue.Key.StartsWith("view:", StringComparison.Ordinal))
        {
            return query;
        }

        var output = Strings(queue.Filters);
        foreach (var (key, value) in query.Where(q => q.Key != "view"))
        {
            output[key] = value;
        }

        return output;
    }

    /// <summary>Remembers the queue for the ticket page, which is opened from the list.</summary>
    public void Remember(HttpContext context, AgentQueue queue, string path) =>
        context.Response.Cookies.Append(QueueCookie, queue.Key, new CookieOptions
        {
            Path = path,
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = context.Request.IsHttps,
            IsEssential = true,
        });

    /// <summary>The queue last shown in the list, or All tickets. The cookie only names a queue.</summary>
    public async Task<AgentQueue> RecalledAsync(HttpContext context, string? agentId, CancellationToken ct)
    {
        var key = context.Request.Cookies[QueueCookie];
        if (key is not null && key.StartsWith("view:", StringComparison.Ordinal))
        {
            // The view may have been deleted or unshared since.
            var id = key["view:".Length..];
            var view = (await ViewsAsync(agentId, ct)).FirstOrDefault(v => Id(v.Id) == id);
            return view is null ? WorkspaceQueue("all", agentId) : ViewQueue(view);
        }

        return WorkspaceQueue(key ?? "all", agentId);
    }

    /// <summary>The <c>inbox</c> prop for this agent and queue.</summary>
    public async Task<Dictionary<string, object?>> InboxAsync(string? agentId, AgentQueue queue, CancellationToken ct)
    {
        var seconds = _options.Ui.AgentInboxCacheSeconds;
        if (seconds <= 0)
        {
            return await BuildAsync(agentId, queue, ct);
        }

        var key = $"{agentId}|{queue.Key}|{string.Join("&", Comparable(queue.Filters).Select(p => $"{p.Key}={p.Value}"))}";
        if (_cache.TryGet(key, out var cached))
        {
            return cached;
        }

        var inbox = await BuildAsync(agentId, queue, ct);
        _cache.Set(key, inbox, TimeSpan.FromSeconds(seconds));
        return inbox;
    }

    private async Task<Dictionary<string, object?>> BuildAsync(string? agentId, AgentQueue queue, CancellationToken ct)
    {
        var counts = new Dictionary<string, object?>
        {
            ["mine"] = agentId is null ? 0 : await CountOpenAsync(Filters(("assigned_to", agentId)), ct),
            ["unassigned"] = await CountOpenAsync(Filters(("unassigned", 1)), ct),
            ["all"] = await CountOpenAsync(Filters(), ct),
        };

        var views = new List<Dictionary<string, object?>>();
        foreach (var view in await ViewsAsync(agentId, ct))
        {
            views.Add(new Dictionary<string, object?>
            {
                ["id"] = view.Id,
                ["name"] = view.Name,
                ["count"] = await CountOpenAsync(view.Filters, ct),
                ["filters"] = view.Filters,
            });
        }

        // Each stat is the list the tile opens: the queue plus the tile's filters.
        var stats = new Dictionary<string, object?>
        {
            ["open"] = await CountAsync(With(queue.Filters, ("status", "open")), ct),
            ["urgent"] = await CountAsync(With(queue.Filters, ("priority", "urgent"), ("open", 1)), ct),
            ["sla_at_risk"] = await CountAsync(With(queue.Filters, ("sla_at_risk", 1)), ct),
            ["resolved_today"] = await CountAsync(With(queue.Filters, ("status", "resolved"), ("resolved_today", 1)), ct),
        };

        // Of the same open tickets, those unread for this agent (ADR 2026-11-05-agent-panel-data).
        var unread = new Dictionary<string, object?>
        {
            ["mine"] = agentId is null ? 0 : await CountUnreadAsync(Filters(("assigned_to", agentId)), agentId, ct),
            ["unassigned"] = await CountUnreadAsync(Filters(("unassigned", 1)), agentId, ct),
            ["all"] = await CountUnreadAsync(Filters(), agentId, ct),
        };

        return new Dictionary<string, object?> { ["counts"] = counts, ["unread"] = unread, ["views"] = views, ["stats"] = stats };
    }

    /// <summary>Built-in views, then the agent's own and shared saved views, by position.</summary>
    public async Task<List<AgentInboxView>> ViewsAsync(string? agentId, CancellationToken ct)
    {
        if (_views is not null)
        {
            return _views;
        }

        var views = new List<AgentInboxView>
        {
            new("urgent", "Urgent", Filters(("priority", "urgent"), ("open", 1))),
            new("sla_at_risk", "SLA at risk", Filters(("sla_at_risk", 1))),
            new("waiting_on_me", "Waiting on me", Filters(("assigned_to", agentId), ("status", "waiting_on_agent"))),
            new("waiting_on_customer", "Waiting on customer", Filters(("assigned_to", agentId), ("status", "waiting_on_customer"))),
            new("recently_updated", "Recently updated", Filters(("open", 1), ("updated_within_hours", 24), ("sort_by", "updated_at"))),
        };

        var saved = await _db.SavedViews.AsNoTracking()
            .Where(v => v.UserId == agentId || v.IsShared)
            .OrderBy(v => v.Position).ThenBy(v => v.Id)
            .Take(MaxSavedViews)
            .ToListAsync(ct);
        views.AddRange(saved.Select(v => new AgentInboxView(v.Id, v.Name, SavedFilters(v.Filters, agentId))));

        return _views = views;
    }

    private static AgentQueue ViewQueue(AgentInboxView view) => new($"view:{Id(view.Id)}", view.Name, view.Filters);

    private static AgentQueue WorkspaceQueue(string key, string? agentId) => key switch
    {
        "mine" when agentId is not null => new("mine", QueueTitles["mine"], Filters(("assigned_to", agentId))),
        "unassigned" => new("unassigned", QueueTitles["unassigned"], Filters(("unassigned", 1))),
        _ => new("all", QueueTitles["all"], Filters()),
    };

    /// <summary>Unresolved, unsnoozed tickets, unless the filters ask for a status themselves.</summary>
    private Task<int> CountOpenAsync(IReadOnlyDictionary<string, object?> filters, CancellationToken ct) =>
        filters.TryGetValue("status", out var status) && !string.IsNullOrWhiteSpace(status?.ToString())
            ? CountAsync(filters, ct)
            : CountAsync(With(filters, ("open", 1)), ct);

    private Task<int> CountAsync(IReadOnlyDictionary<string, object?> filters, CancellationToken ct) =>
        _tickets.CountAsync(TicketListQuery.Parse(Strings(filters), null), ct);

    /// <summary>Of the open tickets <see cref="CountOpenAsync"/> counts, those unread for the agent.</summary>
    private Task<int> CountUnreadAsync(IReadOnlyDictionary<string, object?> filters, string? agentId, CancellationToken ct)
    {
        if (agentId is null) return Task.FromResult(0);
        var parsed = TicketListQuery.Parse(Strings(With(filters, ("open", 1))), null);
        parsed.UnreadFor = agentId;
        return _tickets.CountAsync(parsed, ct);
    }

    private static Dictionary<string, object?> SavedFilters(string json, string? agentId)
    {
        var filters = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (UiPresenter.ParseJson(json) is JsonElement { ValueKind: JsonValueKind.Object } parsed)
        {
            foreach (var property in parsed.EnumerateObject())
            {
                filters[property.Name] = property.Value;
            }
        }

        if (agentId is not null && (Text(filters.GetValueOrDefault("assigned")) == "me" || Text(filters.GetValueOrDefault("assigned_to")) == "me"))
        {
            filters["assigned_to"] = agentId;
        }

        filters.Remove("assigned");
        return filters;
    }

    private static Dictionary<string, object?> Filters(params (string Key, object? Value)[] pairs)
    {
        var filters = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in pairs)
        {
            filters[key] = value;
        }

        return filters;
    }

    private static Dictionary<string, object?> With(IReadOnlyDictionary<string, object?> filters, params (string Key, object? Value)[] extra)
    {
        var output = new Dictionary<string, object?>(filters, StringComparer.Ordinal);
        foreach (var (key, value) in extra)
        {
            output[key] = value;
        }

        return output;
    }

    private static Dictionary<string, string?> Strings(IReadOnlyDictionary<string, object?> filters) =>
        filters.ToDictionary(f => f.Key, f => Text(f.Value), StringComparer.Ordinal);

    private static string? Text(object? value) => value switch
    {
        null => null,
        string s => s,
        bool b => b ? "1" : "0",
        JsonElement { ValueKind: JsonValueKind.String } e => e.GetString(),
        JsonElement { ValueKind: JsonValueKind.True } => "1",
        JsonElement { ValueKind: JsonValueKind.False } => "0",
        JsonElement { ValueKind: JsonValueKind.Null } => null,
        JsonElement { ValueKind: JsonValueKind.Array } e => string.Join(",", e.EnumerateArray().Select(i => Text(i))),
        JsonElement e => e.ToString(),
        IEnumerable<object?> list => string.Join(",", list.Select(Text)),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    private static string Id(object id) => Text(id) ?? string.Empty;

    /// <summary>Filters as strings, without empty values or order, for comparing a URL with a view.</summary>
    private static SortedDictionary<string, string> Comparable(IReadOnlyDictionary<string, object?> filters)
    {
        var clean = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in filters)
        {
            if (PresentationKeys.Contains(key) || value is false)
            {
                continue;
            }

            var text = value switch
            {
                IEnumerable<object?> list => string.Join(",", list.Select(Text).OrderBy(v => v, StringComparer.Ordinal)),
                JsonElement { ValueKind: JsonValueKind.Array } e => string.Join(",", e.EnumerateArray().Select(i => Text(i)).OrderBy(v => v, StringComparer.Ordinal)),
                _ => Text(value),
            };
            if (!string.IsNullOrEmpty(text))
            {
                clean[key] = text!;
            }
        }

        return clean;
    }

    private static bool Same(SortedDictionary<string, string> a, SortedDictionary<string, string> b) =>
        a.Count == b.Count && a.All(p => b.TryGetValue(p.Key, out var v) && v == p.Value);
}

/// <summary>
/// The agent inbox counts, kept per agent and queue in this process for a few
/// seconds. Reads the clock from the registered <see cref="TimeProvider"/>.
/// </summary>
public sealed class AgentInboxCache
{
    private readonly ConcurrentDictionary<string, (DateTimeOffset Expires, Dictionary<string, object?> Inbox)> _entries = new();
    private readonly TimeProvider _time;

    public AgentInboxCache(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
    }

    public bool TryGet(string key, out Dictionary<string, object?> inbox)
    {
        if (_entries.TryGetValue(key, out var entry) && entry.Expires > _time.GetUtcNow())
        {
            inbox = entry.Inbox;
            return true;
        }

        inbox = null!;
        return false;
    }

    public void Set(string key, Dictionary<string, object?> inbox, TimeSpan lifetime)
    {
        var now = _time.GetUtcNow();
        foreach (var expired in _entries.Where(e => e.Value.Expires <= now).Select(e => e.Key).ToList())
        {
            _entries.TryRemove(expired, out _);
        }

        _entries[key] = (now + lifetime, inbox);
    }
}
