using System.Globalization;
using Escalated.Models;
using Escalated.Ui;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Controllers.Ui;

/// <summary>
/// <c>Escalated/Admin/Reports/AgentDetail</c>: one agent over the period, against
/// the period of the same length before it, at escalated-laravel's route. It sends
/// the props the component declares (<c>agent</c>, <c>total_tickets</c>, ...), as
/// escalated-rails does: escalated-laravel's own props for this page
/// (<c>agent_id</c>, <c>metrics</c>, <c>percentiles</c>) are not ones it reads.
/// </summary>
public partial class ReportPageController
{
    private static readonly int[] AgentPercentiles = { 50, 75, 90, 95, 99 };

    /// <summary>One agent's figures for the tickets created in <c>[start, end]</c>.</summary>
    private async Task<Dictionary<string, object?>> AgentFiguresAsync(string agentId, DateTime start, DateTime end, CancellationToken ct)
    {
        var tickets = await _db.Tickets.AsNoTracking()
            .Where(t => t.AssignedTo == agentId && t.CreatedAt >= start && t.CreatedAt <= end)
            .ToListAsync(ct);
        var ratings = await _db.SatisfactionRatings.AsNoTracking()
            .Where(r => r.CreatedAt >= start && r.CreatedAt <= end && r.Ticket != null && r.Ticket.AssignedTo == agentId)
            .Select(r => new { r.Rating })
            .ToListAsync(ct);
        var replies = await _db.Replies.AsNoTracking()
            .Where(r => r.AuthorId == agentId && r.CreatedAt >= start && r.CreatedAt <= end)
            .Select(r => r.CreatedAt)
            .ToListAsync(ct);

        var response = tickets.Where(t => t.FirstResponseAt is not null)
            .Select(t => (t.FirstResponseAt!.Value - t.CreatedAt).TotalHours)
            .OrderBy(h => h).ToList();

        var days = new SortedDictionary<string, int[]>(StringComparer.Ordinal);
        int[] Day(DateTime at)
        {
            var key = Date(at.Date);
            if (!days.TryGetValue(key, out var counts)) days[key] = counts = new int[2];
            return counts;
        }

        foreach (var at in replies) Day(at)[0]++;
        foreach (var ticket in tickets.Where(t => t.ResolvedAt is { } resolved && resolved >= start && resolved <= end))
        {
            Day(ticket.ResolvedAt!.Value)[1]++;
        }

        return new Dictionary<string, object?>
        {
            ["total_tickets"] = tickets.Count,
            ["resolved_tickets"] = tickets.Count(t => t.ResolvedAt is not null),
            ["avg_frt"] = AverageHours(tickets, t => t.FirstResponseAt) ?? 0,
            ["avg_resolution"] = AverageHours(tickets, t => t.ResolvedAt) ?? 0,
            ["csat_score"] = ratings.Count == 0 ? null : Round(ratings.Average(r => (double)r.Rating)),
            ["response_percentiles"] = AgentPercentiles.ToDictionary(p => $"p{p}", p => (object?)Round(Percentile(response, p))),
            ["productivity"] = days.Select(day => new Dictionary<string, object?>
            {
                ["label"] = day.Key,
                ["value"] = day.Value[0] + day.Value[1],
                ["values"] = day.Value,
            }).ToList(),
            ["workload"] = Volume(tickets),
            ["csat_breakdown"] = ratings.GroupBy(r => r.Rating).OrderBy(g => g.Key)
                .Select(g => new Dictionary<string, object?>
                {
                    ["label"] = $"{g.Key.ToString(CultureInfo.InvariantCulture)} stars",
                    ["value"] = g.Count(),
                }).ToList(),
        };
    }

    [HttpGet("agent/{id}/detail")]
    public async Task<IActionResult> AgentDetail(string id, CancellationToken ct)
    {
        var days = Days();
        var end = DateTime.UtcNow;
        var start = end.AddDays(-days);
        var current = await AgentFiguresAsync(id, start, end, ct);
        var previous = await AgentFiguresAsync(id, start.AddDays(-days), start, ct);

        var props = new Dictionary<string, object?>
        {
            ["period_days"] = days,
            ["agent"] = await _present.UserAsync(id, ct),
        };
        foreach (var (key, value) in current) props[key] = value;
        foreach (var key in new[] { "total_tickets", "resolved_tickets", "avg_frt", "avg_resolution", "csat_score" })
        {
            props[$"prev_{key}"] = previous[key];
        }

        return UiResults.Render("Escalated/Admin/Reports/AgentDetail", props);
    }
}
