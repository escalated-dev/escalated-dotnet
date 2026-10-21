using System.Globalization;
using Escalated.Enums;
using Escalated.Models;
using Escalated.Ui;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Controllers.Ui;

/// <summary>
/// The advanced report pages: <c>ResponseTimes</c>, <c>ResolutionTimes</c>,
/// <c>AgentRanking</c>, <c>SlaTrends</c> and <c>Cohorts</c>, with the figures
/// escalated-laravel's <c>ReportingService</c> computes and the shapes its
/// <c>ReportScreens</c> hands the pages (every chart reads <c>{label, value}</c>).
/// </summary>
public partial class ReportPageController
{
    private const double FirstResponseTargetHours = 4;
    private const double ResolutionTargetHours = 24;

    private static readonly (string Label, double Min, double? Max)[] FirstResponseBuckets =
    {
        ("<1h", 0, 1), ("1-4h", 1, 4), ("4-8h", 4, 8), ("8-24h", 8, 24), (">24h", 24, null),
    };

    private static readonly (string Label, double Min, double? Max)[] ResolutionBuckets =
    {
        ("<4h", 0, 4), ("4-8h", 4, 8), ("8-24h", 8, 24), ("1-3d", 24, 72), ("3-7d", 72, 168), (">7d", 168, null),
    };

    private Task<List<Ticket>> SinceAsync(int days, CancellationToken ct)
    {
        var since = DateTime.UtcNow.AddDays(-days);
        return _db.Tickets.AsNoTracking().Where(t => t.CreatedAt >= since).ToListAsync(ct);
    }

    private static double Hours(Ticket ticket, DateTime at) => (at - ticket.CreatedAt).TotalHours;

    /// <summary>The percentile by linear interpolation between ranks, as Laravel's <c>percentile()</c>.</summary>
    private static double Percentile(IReadOnlyList<double> sorted, double percentile)
    {
        if (sorted.Count == 0) return 0;
        var index = percentile / 100 * (sorted.Count - 1);
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);
        return lower == upper ? sorted[lower] : sorted[lower] + (index - lower) * (sorted[upper] - sorted[lower]);
    }

    private static Dictionary<string, object?> Point(string label, object value) => new() { ["label"] = label, ["value"] = value };

    /// <summary>A null or blank group reads <c>unknown</c>, as <c>ReportScreens::label()</c>.</summary>
    private static string Label(string? value) => string.IsNullOrEmpty(value) ? "unknown" : value;

    private async Task<HashSet<int>> ChatTicketIdsAsync(IEnumerable<Ticket> tickets, CancellationToken ct)
    {
        var ids = tickets.Select(t => t.Id).ToList();
        return (await _db.ChatSessions.AsNoTracking().Where(s => ids.Contains(s.TicketId)).Select(s => s.TicketId).Distinct().ToListAsync(ct)).ToHashSet();
    }

    private async Task<Dictionary<int, string>> DepartmentNamesAsync(CancellationToken ct) =>
        await _db.Departments.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Name, ct);

    /// <summary>The page props for the first-response or resolution report.</summary>
    private async Task<Dictionary<string, object?>> TimesAsync(int days, Func<Ticket, DateTime?> at, double target,
        (string Label, double Min, double? Max)[] buckets, string prefix, string thirdGroup, CancellationToken ct)
    {
        var tickets = (await SinceAsync(days, ct)).Where(t => at(t) is not null).ToList();
        var hours = tickets.Select(t => Hours(t, at(t)!.Value)).OrderBy(h => h).ToList();
        var departments = await DepartmentNamesAsync(ct);
        var chat = thirdGroup == "by_channel" ? await ChatTicketIdsAsync(tickets, ct) : new HashSet<int>();

        Dictionary<string, object?> Stats(IEnumerable<Ticket> group)
        {
            var values = group.Select(t => Hours(t, at(t)!.Value)).OrderBy(h => h).ToList();
            return new Dictionary<string, object?>
            {
                ["count"] = values.Count,
                ["avg"] = Round(values.Average()),
                ["median"] = Round(Percentile(values, 50)),
                ["p90"] = Round(Percentile(values, 90)),
            };
        }

        var byAgent = new List<Dictionary<string, object?>>();
        foreach (var group in tickets.Where(t => !string.IsNullOrEmpty(t.AssignedTo)).GroupBy(t => t.AssignedTo!).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var row = new Dictionary<string, object?>
            {
                ["agent_id"] = group.Key,
                ["agent_name"] = (await _present.UserAsync(group.Key, ct))?["name"] ?? group.Key,
            };
            foreach (var (key, value) in Stats(group)) row[key] = value;
            byAgent.Add(row);
        }

        var props = new Dictionary<string, object?>
        {
            ["period_days"] = days,
            [$"avg_{prefix}"] = hours.Count == 0 ? 0.0 : Round(hours.Average()),
            [$"median_{prefix}"] = Round(Percentile(hours, 50)),
            [$"p90_{prefix}"] = Round(Percentile(hours, 90)),
            ["pct_under_target"] = hours.Count == 0 ? 0.0 : Round(hours.Count(h => h <= target) * 100.0 / hours.Count),
            ["target_hours"] = target,
            ["distribution"] = buckets.Select(b => Point(b.Label, hours.Count(h => h >= b.Min && (b.Max is null || h < b.Max)))).ToList(),
            ["trend"] = tickets.GroupBy(t => t.CreatedAt.Date).OrderBy(g => g.Key)
                .Select(g => Point(Date(g.Key), Round(g.Average(t => Hours(t, at(t)!.Value))))).ToList(),
            ["by_agent"] = byAgent,
            ["by_department"] = tickets.GroupBy(t => t.DepartmentId).OrderBy(g => g.Key ?? int.MaxValue)
                .Select(g => Point(g.Key is { } id && departments.TryGetValue(id, out var name) ? name : "Unassigned", Stats(g)["avg"]!)).ToList(),
        };

        props[thirdGroup] = thirdGroup == "by_channel"
            ? tickets.GroupBy(t => chat.Contains(t.Id) ? "chat" : null).OrderBy(g => g.Key ?? string.Empty, StringComparer.Ordinal)
                .Select(g => Point(Label(g.Key), Stats(g)["avg"]!)).ToList()
            : tickets.GroupBy(t => t.Priority.ToValue()).OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => Point(g.Key, Stats(g)["avg"]!)).ToList();
        return props;
    }

    [HttpGet("response-times")]
    [HttpGet("frt")]
    public async Task<IActionResult> ResponseTimes(CancellationToken ct) =>
        UiResults.Render("Escalated/Admin/Reports/ResponseTimes",
            await TimesAsync(Days(), t => t.FirstResponseAt, FirstResponseTargetHours, FirstResponseBuckets, "frt", "by_priority", ct));

    [HttpGet("resolution-times")]
    [HttpGet("resolution")]
    public async Task<IActionResult> ResolutionTimes(CancellationToken ct) =>
        UiResults.Render("Escalated/Admin/Reports/ResolutionTimes",
            await TimesAsync(Days(), t => t.ResolvedAt, ResolutionTargetHours, ResolutionBuckets, "resolution", "by_channel", ct));

    /// <summary>
    /// Agents ranked by a composite score: resolution rate 30%, first-response
    /// score 25%, CSAT 25%, volume 20%, as Laravel's <c>agentPerformanceRanking()</c>.
    /// </summary>
    [HttpGet("agent-ranking")]
    public async Task<IActionResult> AgentRanking(CancellationToken ct)
    {
        var days = Days();
        var since = DateTime.UtcNow.AddDays(-days);
        var tickets = await SinceAsync(days, ct);
        var ratings = await _db.SatisfactionRatings.AsNoTracking().Where(r => r.CreatedAt >= since)
            .Join(_db.Tickets.AsNoTracking(), r => r.TicketId, t => t.Id, (r, t) => new { r.Rating, t.AssignedTo })
            .ToListAsync(ct);

        var rows = new List<Dictionary<string, object?>>();
        foreach (var agent in await AgentPerformanceAsync(tickets, ct))
        {
            var total = (int)agent["total_tickets"]!;
            var resolved = (int)agent["resolved_tickets"]!;
            var avgResponse = agent["avg_response_hours"] as double? ?? 0;
            var avgResolution = agent["avg_resolution_hours"] as double? ?? 0;
            var agentRatings = ratings.Where(r => r.AssignedTo == (string)agent["agent_id"]!).Select(r => (double)r.Rating).ToList();
            var csat = agentRatings.Count == 0 ? 0 : agentRatings.Average();

            var resolutionRate = total > 0 ? resolved * 100.0 / total : 0;
            var frtScore = avgResponse > 0 ? Math.Max(0, 100 - avgResponse * 5) : 50;
            var composite = Round(resolutionRate * 0.30 + frtScore * 0.25 + csat * 20 * 0.25 + Math.Min(100, total * 2) * 0.20);

            rows.Add(new Dictionary<string, object?>
            {
                ["agent_id"] = agent["agent_id"],
                ["agent_name"] = agent["agent_name"] ?? string.Empty,
                ["volume"] = total,
                ["resolution_rate"] = Round(resolutionRate),
                ["avg_frt"] = avgResponse,
                ["avg_resolution"] = avgResolution,
                ["csat"] = Round(csat),
                ["composite_score"] = composite,
            });
        }

        return UiResults.Render("Escalated/Admin/Reports/AgentRanking", new
        {
            period_days = days,
            agents = rows.OrderByDescending(r => (double)r["composite_score"]!).ToList(),
        });
    }

    [HttpGet("sla-trends")]
    public async Task<IActionResult> SlaTrends(CancellationToken ct)
    {
        var days = Days();
        var groupBy = Request.Query["group_by"].FirstOrDefault();
        var tickets = await SinceAsync(days, ct);
        var breached = tickets.Where(Breached).ToList();
        var covered = tickets.Where(t => t.SlaPolicyId is not null).ToList();
        var departments = await DepartmentNamesAsync(ct);

        string Period(DateTime at) => groupBy switch
        {
            "week" => $"{ISOWeek.GetYear(at)}-W{ISOWeek.GetWeekOfYear(at):00}",
            "month" => at.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            _ => Date(at),
        };

        var periods = breached.GroupBy(t => Period(t.CreatedAt)).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();

        static double Rate(IReadOnlyCollection<Ticket> group) => group.Count == 0 ? 0 : Round(group.Count(Breached) * 100.0 / group.Count);

        return UiResults.Render("Escalated/Admin/Reports/SlaTrends", new
        {
            period_days = days,
            breach_trend = periods.Select(g => Point(g.Key, g.Count())).ToList(),
            breach_by_type_trend = periods.Select(g => new Dictionary<string, object?>
            {
                ["label"] = g.Key,
                ["values"] = new[] { g.Count(t => t.SlaFirstResponseBreached), g.Count(t => t.SlaResolutionBreached) },
            }).ToList(),
            breach_by_department = covered.GroupBy(t => t.DepartmentId).OrderBy(g => g.Key ?? int.MaxValue)
                .Select(g => Point(g.Key is { } id && departments.TryGetValue(id, out var name) ? name : "Unassigned", Rate(g.ToList()))).ToList(),
            breach_by_priority = covered.GroupBy(t => t.Priority.ToValue()).OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => Point(g.Key, Rate(g.ToList()))).ToList(),
            at_risk_tickets = await AtRiskAsync(ct),
            total_breaches = breached.Count,
            breach_rate = tickets.Count > 0 ? Round(breached.Count * 100.0 / tickets.Count) : 0.0,
            first_response_breaches = tickets.Count(t => t.SlaFirstResponseBreached),
            resolution_breaches = tickets.Count(t => t.SlaResolutionBreached),
        });
    }

    /// <summary>Open tickets with an SLA deadline in the next 8 hours, soonest first (at most 50).</summary>
    private async Task<List<Dictionary<string, object?>>> AtRiskAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var deadline = now.AddHours(8);
        var tickets = await _db.Tickets.AsNoTracking()
            .Where(t => t.Status != TicketStatus.Resolved && t.Status != TicketStatus.Closed)
            .Where(t => (t.FirstResponseAt == null && !t.SlaFirstResponseBreached && t.FirstResponseDueAt >= now && t.FirstResponseDueAt <= deadline)
                || (t.ResolvedAt == null && !t.SlaResolutionBreached && t.ResolutionDueAt >= now && t.ResolutionDueAt <= deadline))
            .ToListAsync(ct);

        return tickets.Select(t =>
            {
                var due = new[]
                    {
                        t.FirstResponseAt is null ? t.FirstResponseDueAt : null,
                        t.ResolvedAt is null ? t.ResolutionDueAt : null,
                    }
                    .Where(at => at is { } value && value >= now && value <= deadline)
                    .Min()!.Value;
                return new Dictionary<string, object?>
                {
                    ["id"] = t.Id,
                    ["reference"] = t.Reference,
                    ["subject"] = t.Subject,
                    ["hours_remaining"] = Round(Math.Floor((due - now).TotalMinutes) / 60),
                };
            })
            .OrderBy(r => (double)r["hours_remaining"]!)
            .Take(50)
            .ToList();
    }

    [HttpGet("cohorts")]
    public async Task<IActionResult> Cohorts(CancellationToken ct)
    {
        var days = Days();
        var tickets = await SinceAsync(days, ct);
        var ids = tickets.Select(t => t.Id).ToList();
        var departments = await DepartmentNamesAsync(ct);
        var chat = await ChatTicketIdsAsync(tickets, ct);
        var tagged = await _db.TicketTags.AsNoTracking().Where(tt => ids.Contains(tt.TicketId))
            .Join(_db.Tags.AsNoTracking(), tt => tt.TagId, tag => tag.Id, (tt, tag) => new { tt.TicketId, tag.Id, tag.Name })
            .ToListAsync(ct);
        var byId = tickets.ToDictionary(t => t.Id);

        static Dictionary<string, object?> Cohort(string name, IReadOnlyCollection<Ticket> group) => new()
        {
            ["name"] = name,
            ["volume"] = group.Count,
            ["avg_resolution"] = AverageHours(group, t => t.ResolvedAt) ?? 0,
            ["breach_rate"] = group.Count > 0 ? Round(group.Count(Breached) * 100.0 / group.Count) : 0,
            ["csat"] = 0,
        };

        return UiResults.Render("Escalated/Admin/Reports/Cohorts", new
        {
            period_days = days,
            by_tag = tagged.GroupBy(t => (t.Id, t.Name)).OrderBy(g => g.Key.Name, StringComparer.Ordinal)
                .Select(g => Cohort(g.Key.Name, g.Select(x => byId[x.TicketId]).ToList())).ToList(),
            by_department = tickets.GroupBy(t => t.DepartmentId).OrderBy(g => g.Key ?? int.MaxValue)
                .Select(g => Cohort(g.Key is { } id && departments.TryGetValue(id, out var name) ? name : "Unassigned", g.ToList())).ToList(),
            by_channel = tickets.GroupBy(t => chat.Contains(t.Id) ? "chat" : null).OrderBy(g => g.Key ?? string.Empty, StringComparer.Ordinal)
                .Select(g => Cohort(Label(g.Key), g.ToList())).ToList(),
            by_type = tickets.GroupBy(t => t.TicketType ?? "unspecified").OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => Cohort(g.Key, g.ToList())).ToList(),
            by_priority = tickets.GroupBy(t => t.Priority.ToValue()).OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => Cohort(g.Key, g.ToList())).ToList(),
        });
    }
}
