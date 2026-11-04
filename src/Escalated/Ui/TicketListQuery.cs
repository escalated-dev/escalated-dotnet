using Escalated.Enums;
using Escalated.Services;
using Microsoft.AspNetCore.Http;

namespace Escalated.Ui;

/// <summary>
/// The ticket list's query string read into <see cref="TicketListFilters"/>. The
/// queue pages and the agent panel's inbox counts both go through here, so a count
/// and the list it labels cannot disagree about what a filter means.
/// </summary>
public static class TicketListQuery
{
    private static readonly HashSet<string> Statuses = Enum.GetValues<TicketStatus>().Select(s => s.ToValue()).ToHashSet();
    private static readonly HashSet<string> Priorities = Enum.GetValues<TicketPriority>().Select(p => p.ToValue()).ToHashSet();
    private static readonly string[] Flags = ["open", "sla_at_risk", "resolved_today"];

    /// <summary>The query string as one value per key (repeated values joined with commas).</summary>
    public static Dictionary<string, string?> From(IQueryCollection query) =>
        query.ToDictionary(q => q.Key, q => (string?)q.Value.ToString(), StringComparer.Ordinal);

    /// <summary>
    /// Checks the filters added for the agent panel: <c>open</c>, <c>sla_at_risk</c>
    /// and <c>resolved_today</c> are booleans, <c>updated_within_hours</c> an
    /// integer from 1 to 8760. Empty when they are valid.
    /// </summary>
    public static Dictionary<string, string> Validate(IReadOnlyDictionary<string, string?> query)
    {
        var errors = new Dictionary<string, string>();
        foreach (var key in Flags)
        {
            if (Get(query, key) is { } value && value.ToLowerInvariant() is not ("0" or "1" or "true" or "false"))
            {
                errors[key] = $"The {key} field must be true or false.";
            }
        }

        if (Get(query, "updated_within_hours") is { } hours
            && (!int.TryParse(hours, out var n) || n < 1 || n > 8760))
        {
            errors["updated_within_hours"] = "The updated_within_hours field must be an integer between 1 and 8760.";
        }

        return errors;
    }

    /// <summary>The store filters for a list query. <paramref name="currentUserId"/> backs <c>following</c>.</summary>
    public static TicketListFilters Parse(IReadOnlyDictionary<string, string?> query, string? currentUserId)
    {
        var now = DateTime.UtcNow;
        return new TicketListFilters
        {
            Status = Get(query, "status") is { } status && Statuses.Contains(status) ? TicketStatusExtensions.Parse(status) : null,
            Priority = Get(query, "priority") is { } priority && Priorities.Contains(priority) ? TicketPriorityExtensions.Parse(priority) : null,
            TicketType = Get(query, "ticket_type"),
            AssignedTo = Get(query, "assigned_to"),
            Unassigned = Truthy(query, "unassigned") ? true : null,
            DepartmentId = int.TryParse(Get(query, "department_id"), out var departmentId) ? departmentId : null,
            Search = Get(query, "search"),
            SlaBreach = Truthy(query, "sla_breached") ? true : null,
            SortBy = Get(query, "sort_by"),
            SortDir = Get(query, "sort_dir"),
            TagIds = TagIds(query),
            FollowedBy = Truthy(query, "following") ? currentUserId : null,
            Open = Truthy(query, "open") ? true : null,
            SlaAtRisk = Truthy(query, "sla_at_risk") ? true : null,
            ResolvedSince = Truthy(query, "resolved_today") ? now.Date : null,
            UpdatedSince = int.TryParse(Get(query, "updated_within_hours"), out var hours) && hours is > 0 and <= 8760
                ? now.AddHours(-hours) : null,
        };
    }

    internal static string? Get(IReadOnlyDictionary<string, string?> query, string key) =>
        query.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    internal static bool Truthy(IReadOnlyDictionary<string, string?> query, string key) =>
        Get(query, key)?.ToLowerInvariant() is "1" or "true";

    private static int[]? TagIds(IReadOnlyDictionary<string, string?> query)
    {
        var ids = new[] { "tag_ids", "tag_ids[]", "tag" }
            .Select(key => Get(query, key) ?? string.Empty)
            .SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries))
            .Select(v => int.TryParse(v, out var id) ? id : (int?)null)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .ToArray();
        return ids.Length > 0 ? ids : null;
    }
}
