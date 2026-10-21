using System.Globalization;
using Escalated.Authorization;
using Escalated.Data;
using Escalated.Enums;
using Escalated.Models;
using Escalated.Ui;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Controllers.Ui;

/// <summary>
/// The report pages, with escalated-laravel's <c>ReportController</c> routes and
/// the figures its <c>ReportingService</c> computes: the overview
/// (<c>Escalated/Admin/Reports</c>), <c>Reports/Dashboard</c>, <c>AgentMetrics</c>,
/// <c>SlaReport</c>, <c>CsatReport</c> and <c>Comparison</c>. Every report covers the
/// last <c>days</c> (default 30). <c>GET support/admin/reports</c>, <c>/sla</c> and
/// <c>/csat</c> are also JSON API routes, so those answer only page requests.
/// </summary>
[Route("support/admin/reports")]
[Authorize(Policy = EscalatedPolicies.Admin)]
[EscalatedPage]
public partial class ReportPageController : ControllerBase
{
    private readonly EscalatedDbContext _db;
    private readonly UiPresenter _present;

    public ReportPageController(EscalatedDbContext db, UiPresenter present)
    {
        _db = db;
        _present = present;
    }

    /// <summary>The period in days: <c>period</c>, then <c>days</c>, as Laravel reads them; 1 to 365.</summary>
    private int Days()
    {
        var raw = Request.Query["period"].FirstOrDefault() ?? Request.Query["days"].FirstOrDefault();
        return Math.Clamp(int.TryParse(raw, out var days) ? days : 30, 1, 365);
    }

    private Task<List<Ticket>> TicketsAsync(DateTime start, DateTime end, CancellationToken ct) =>
        _db.Tickets.AsNoTracking().Where(t => t.CreatedAt >= start && t.CreatedAt <= end).ToListAsync(ct);

    private Task<List<SatisfactionRating>> RatingsAsync(DateTime start, DateTime end, CancellationToken ct) =>
        _db.SatisfactionRatings.AsNoTracking().Where(r => r.CreatedAt >= start && r.CreatedAt <= end).ToListAsync(ct);

    private static bool Breached(Ticket ticket) => ticket.SlaFirstResponseBreached || ticket.SlaResolutionBreached;

    private static double Round(double value) => Math.Round(value, 1, MidpointRounding.AwayFromZero);

    /// <summary>The mean hours from creation to <paramref name="at"/> over the tickets that have it, or null with none.</summary>
    private static double? AverageHours(IEnumerable<Ticket> tickets, Func<Ticket, DateTime?> at)
    {
        var hours = tickets.Where(t => at(t) is not null).Select(t => (at(t)!.Value - t.CreatedAt).TotalHours).ToList();
        return hours.Count == 0 ? null : Round(hours.Average());
    }

    /// <summary>Percent of the tickets under an SLA policy that met it; 100 with none.</summary>
    private static double SlaCompliance(IReadOnlyCollection<Ticket> tickets)
    {
        var withPolicy = tickets.Where(t => t.SlaPolicyId is not null).ToList();
        return withPolicy.Count == 0 ? 100.0 : Round((withPolicy.Count - withPolicy.Count(Breached)) * 100.0 / withPolicy.Count);
    }

    private static double CsatAverage(IReadOnlyCollection<SatisfactionRating> ratings) =>
        ratings.Count == 0 ? 0 : Round(ratings.Average(r => (double)r.Rating));

    private static string Date(DateTime value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary><c>{label: date, value: count}</c> for each day with tickets, oldest first.</summary>
    private static List<Dictionary<string, object?>> Volume(IEnumerable<Ticket> tickets) =>
        tickets.GroupBy(t => t.CreatedAt.Date).OrderBy(g => g.Key)
            .Select(g => new Dictionary<string, object?> { ["label"] = Date(g.Key), ["value"] = g.Count() }).ToList();

    private static List<Dictionary<string, object?>> LabelValues<TKey>(IEnumerable<Ticket> tickets, Func<Ticket, TKey> key, Func<TKey, string> label) =>
        tickets.GroupBy(key).OrderBy(g => label(g.Key), StringComparer.Ordinal)
            .Select(g => new Dictionary<string, object?> { ["label"] = label(g.Key), ["value"] = g.Count() }).ToList();

    /// <summary>
    /// Per assigned agent: <c>agent_id</c>, <c>agent_name</c>, <c>total_tickets</c>,
    /// <c>resolved_tickets</c>, <c>avg_response_hours</c>, <c>avg_resolution_hours</c>.
    /// </summary>
    private async Task<List<Dictionary<string, object?>>> AgentPerformanceAsync(IEnumerable<Ticket> tickets, CancellationToken ct)
    {
        var rows = new List<Dictionary<string, object?>>();
        foreach (var group in tickets.Where(t => t.AssignedTo is not null).GroupBy(t => t.AssignedTo!).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            rows.Add(new Dictionary<string, object?>
            {
                ["agent_id"] = group.Key,
                ["agent_name"] = (await _present.UserAsync(group.Key, ct))?["name"],
                ["total_tickets"] = group.Count(),
                ["resolved_tickets"] = group.Count(t => t.ResolvedAt is not null),
                ["avg_response_hours"] = AverageHours(group, t => t.FirstResponseAt),
                ["avg_resolution_hours"] = AverageHours(group, t => t.ResolvedAt),
            });
        }

        return rows;
    }

    [HttpGet("")]
    [EscalatedPage(Negotiate = true)]
    public async Task<IActionResult> Overview(CancellationToken ct)
    {
        var days = Days();
        var since = DateTime.UtcNow.AddDays(-days);
        var tickets = await _db.Tickets.AsNoTracking().Where(t => t.CreatedAt >= since).ToListAsync(ct);
        var ratings = await _db.SatisfactionRatings.AsNoTracking().Where(r => r.CreatedAt >= since).ToListAsync(ct);

        return UiResults.Render("Escalated/Admin/Reports", new
        {
            period_days = days,
            total_tickets = tickets.Count,
            resolved_tickets = await _db.Tickets.CountAsync(t => t.ResolvedAt != null && t.ResolvedAt >= since, ct),
            avg_first_response_hours = AverageHours(tickets, t => t.FirstResponseAt) ?? 0,
            sla_breach_count = tickets.Count(Breached),
            by_status = tickets.GroupBy(t => t.Status.ToValue()).ToDictionary(g => g.Key, g => g.Count()),
            by_priority = tickets.GroupBy(t => t.Priority.ToValue()).ToDictionary(g => g.Key, g => g.Count()),
            csat = new Dictionary<string, object?>
            {
                ["average"] = CsatAverage(ratings),
                ["total"] = ratings.Count,
                ["breakdown"] = ratings.GroupBy(r => r.Rating).OrderBy(g => g.Key)
                    .ToDictionary(g => g.Key.ToString(CultureInfo.InvariantCulture), g => g.Count()),
            },
        });
    }

    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard(CancellationToken ct)
    {
        var days = Days();
        var end = DateTime.UtcNow;
        var start = end.AddDays(-days);
        var tickets = await TicketsAsync(start, end, ct);
        var all = await _db.Tickets.AsNoTracking().Select(t => new { t.Status, t.Priority }).ToListAsync(ct);

        return UiResults.Render("Escalated/Admin/Reports/Dashboard", new
        {
            period_days = days,
            volume = Volume(tickets),
            by_status = all.GroupBy(t => t.Status.ToValue()).OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new Dictionary<string, object?> { ["label"] = g.Key, ["value"] = g.Count() }).ToList(),
            by_priority = all.GroupBy(t => t.Priority.ToValue()).OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new Dictionary<string, object?> { ["label"] = g.Key, ["value"] = g.Count() }).ToList(),
            avg_response_hours = AverageHours(tickets, t => t.FirstResponseAt) ?? 0,
            avg_resolution_hours = AverageHours(tickets, t => t.ResolvedAt) ?? 0,
            sla_compliance = SlaCompliance(tickets),
            csat_average = CsatAverage(await RatingsAsync(start, end, ct)),
            agent_performance = await AgentPerformanceAsync(tickets, ct),
        });
    }

    [HttpGet("agents")]
    public async Task<IActionResult> Agents(CancellationToken ct)
    {
        var days = Days();
        var end = DateTime.UtcNow;
        var tickets = await TicketsAsync(end.AddDays(-days), end, ct);

        return UiResults.Render("Escalated/Admin/Reports/AgentMetrics", new
        {
            period_days = days,
            agents = await AgentPerformanceAsync(tickets, ct),
        });
    }

    [HttpGet("sla")]
    [EscalatedPage(Negotiate = true)]
    public async Task<IActionResult> Sla(CancellationToken ct)
    {
        var days = Days();
        var end = DateTime.UtcNow;
        var tickets = await TicketsAsync(end.AddDays(-days), end, ct);
        var policies = await _db.SlaPolicies.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.Id).ToListAsync(ct);
        var policyNames = await _db.SlaPolicies.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Name, ct);

        var breaches = new List<Dictionary<string, object?>>();
        foreach (var ticket in tickets.Where(Breached).OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id).Take(100))
        {
            breaches.Add(new Dictionary<string, object?>
            {
                ["id"] = ticket.Id,
                ["reference"] = ticket.Reference,
                ["subject"] = ticket.Subject,
                ["assigned_to"] = ticket.AssignedTo,
                ["assignee"] = await _present.UserAsync(ticket.AssignedTo, ct),
                ["sla_policy_id"] = ticket.SlaPolicyId,
                ["sla_policy"] = ticket.SlaPolicyId is { } id && policyNames.TryGetValue(id, out var name)
                    ? new Dictionary<string, object?> { ["id"] = id, ["name"] = name }
                    : null,
                ["sla_first_response_breached"] = ticket.SlaFirstResponseBreached,
                ["sla_resolution_breached"] = ticket.SlaResolutionBreached,
                ["first_response_at"] = ticket.FirstResponseAt,
                ["resolved_at"] = ticket.ResolvedAt,
                ["created_at"] = ticket.CreatedAt,
            });
        }

        return UiResults.Render("Escalated/Admin/Reports/SlaReport", new
        {
            period_days = days,
            compliance_rate = SlaCompliance(tickets),
            compliance_by_policy = policies.Select(policy =>
            {
                var covered = tickets.Where(t => t.SlaPolicyId == policy.Id).ToList();
                var breached = covered.Count(Breached);
                return new Dictionary<string, object?>
                {
                    ["policy_name"] = policy.Name,
                    ["total"] = covered.Count,
                    ["met"] = covered.Count - breached,
                    ["breached"] = breached,
                };
            }).ToList(),
            breaches,
        });
    }

    [HttpGet("csat")]
    [EscalatedPage(Negotiate = true)]
    public async Task<IActionResult> Csat(CancellationToken ct)
    {
        var days = Days();
        var end = DateTime.UtcNow;
        var start = end.AddDays(-days);
        var ratings = await RatingsAsync(start, end, ct);
        var totalTickets = await _db.Tickets.CountAsync(t => t.CreatedAt >= start && t.CreatedAt <= end, ct);
        var ticketIds = ratings.Select(r => r.TicketId).Distinct().ToList();
        var assignees = await _db.Tickets.AsNoTracking().Where(t => ticketIds.Contains(t.Id) && t.AssignedTo != null)
            .ToDictionaryAsync(t => t.Id, t => t.AssignedTo!, ct);

        var byAgent = new List<Dictionary<string, object?>>();
        foreach (var group in ratings.Where(r => assignees.ContainsKey(r.TicketId)).GroupBy(r => assignees[r.TicketId]).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            byAgent.Add(new Dictionary<string, object?>
            {
                ["agent_id"] = group.Key,
                ["agent_name"] = (await _present.UserAsync(group.Key, ct))?["name"],
                ["avg_rating"] = Round(group.Average(r => (double)r.Rating)),
                ["total_ratings"] = group.Count(),
            });
        }

        return UiResults.Render("Escalated/Admin/Reports/CsatReport", new
        {
            period_days = days,
            csat_average = CsatAverage(ratings),
            response_rate = totalTickets > 0 ? Round(ratings.Count * 100.0 / totalTickets) : 0,
            total_ratings = ratings.Count,
            by_agent = byAgent,
            over_time = ratings.GroupBy(r => r.CreatedAt.Date).OrderBy(g => g.Key)
                .Select(g => new Dictionary<string, object?> { ["label"] = Date(g.Key), ["value"] = Round(g.Average(r => (double)r.Rating)) })
                .ToList(),
        });
    }

    [HttpGet("comparison")]
    public async Task<IActionResult> Comparison(CancellationToken ct)
    {
        var days = Days();
        var now = DateTime.UtcNow;
        var currentStart = now.AddDays(-days);
        var previousStart = now.AddDays(-days * 2);

        return UiResults.Render("Escalated/Admin/Reports/Comparison", new
        {
            period_days = days,
            current = await PeriodAsync(currentStart, now, ct),
            previous = await PeriodAsync(previousStart, currentStart, ct),
        });
    }

    private async Task<Dictionary<string, object?>> PeriodAsync(DateTime start, DateTime end, CancellationToken ct)
    {
        var tickets = await TicketsAsync(start, end, ct);
        return new Dictionary<string, object?>
        {
            ["total_tickets"] = tickets.Count,
            ["resolved_tickets"] = tickets.Count(t => t.ResolvedAt is not null),
            ["avg_frt"] = AverageHours(tickets, t => t.FirstResponseAt) ?? 0,
            ["avg_resolution"] = AverageHours(tickets, t => t.ResolvedAt) ?? 0,
            ["sla_compliance"] = SlaCompliance(tickets),
            ["csat"] = CsatAverage(await RatingsAsync(start, end, ct)),
            ["breach_count"] = tickets.Count(Breached),
            ["volume_trend"] = Volume(tickets),
        };
    }
}
