using System.Text.Json.Serialization;
using Escalated.Data;
using Escalated.Enums;
using Escalated.Models;
using Escalated.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Controllers.Admin;

/// <summary>
/// Exposes the analytics computed by <see cref="AdvancedReportingService"/> as
/// read-only admin JSON endpoints. The report surface mirrors the canonical
/// Laravel reference (<c>Escalated\Laravel\Http\Controllers\Admin\ReportController</c>
/// plus its API sibling): an overview summary, first-response-time and
/// resolution-time distributions, an agent performance ranking, SLA compliance,
/// CSAT analytics, and a current-vs-previous period comparison.
///
/// All endpoints sit under the <c>support/admin</c> route prefix, so the host
/// app's admin gate (the same middleware that protects every other
/// <c>Admin*Controller</c>) governs access — the plugin does not own the host
/// identity stack.
/// </summary>
[ApiController]
[Route("support/admin/reports")]
public class AdminReportController : ControllerBase
{
    private readonly EscalatedDbContext _db;

    public AdminReportController(EscalatedDbContext db)
    {
        _db = db;
    }

    /// <summary>GET /support/admin/reports — dashboard overview for the period.</summary>
    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] int days = 30, CancellationToken ct = default)
    {
        var (start, end) = Window(days);

        var tickets = await _db.Tickets.AsNoTracking()
            .Where(t => t.CreatedAt >= start && t.CreatedAt <= end)
            .ToListAsync(ct);

        var ratings = await RatingsInWindowAsync(start, end, ct);

        var byStatus = tickets
            .GroupBy(t => t.Status)
            .Select(g => new LabelValue(g.Key.ToValue(), g.Count()))
            .OrderBy(x => x.Label)
            .ToList();

        var byPriority = tickets
            .GroupBy(t => t.Priority)
            .Select(g => new LabelValue(g.Key.ToValue(), g.Count()))
            .OrderBy(x => x.Label)
            .ToList();

        // DateSeries drives the volume axis (one bucket per day, capped at 90).
        var volume = AdvancedReportingService.DateSeries(start.Date, end.Date)
            .Select(d => new LabelValue(
                d.ToString("yyyy-MM-dd"),
                tickets.Count(t => t.CreatedAt.Date == d.Date)))
            .ToList();

        return Ok(new SummaryEnvelope(
            days,
            tickets.Count,
            tickets.Count(t => t.ResolvedAt != null),
            Average(HoursDiffs(tickets, t => t.FirstResponseAt)),
            Average(HoursDiffs(tickets, t => t.ResolvedAt)),
            SlaComplianceRate(tickets),
            ratings.Count == 0 ? 0 : Math.Round(ratings.Average(r => (double)r.Rating), 1),
            byStatus,
            byPriority,
            volume));
    }

    /// <summary>GET /support/admin/reports/first-response-time — FRT distribution + percentiles.</summary>
    [HttpGet("first-response-time")]
    public async Task<IActionResult> FirstResponseTime([FromQuery] int days = 30, CancellationToken ct = default)
    {
        var (start, end) = Window(days);

        var tickets = await _db.Tickets.AsNoTracking()
            .Where(t => t.CreatedAt >= start && t.CreatedAt <= end && t.FirstResponseAt != null)
            .ToListAsync(ct);

        var hours = HoursDiffs(tickets, t => t.FirstResponseAt);

        return Ok(new DistributionEnvelope(
            days,
            hours.Count,
            AdvancedReportingService.BuildDistribution(hours, "hours"),
            AdvancedReportingService.CalculatePercentiles(hours)));
    }

    /// <summary>GET /support/admin/reports/resolution-time — resolution distribution + percentiles.</summary>
    [HttpGet("resolution-time")]
    public async Task<IActionResult> ResolutionTime([FromQuery] int days = 30, CancellationToken ct = default)
    {
        var (start, end) = Window(days);

        var tickets = await _db.Tickets.AsNoTracking()
            .Where(t => t.CreatedAt >= start && t.CreatedAt <= end && t.ResolvedAt != null)
            .ToListAsync(ct);

        var hours = HoursDiffs(tickets, t => t.ResolvedAt);

        return Ok(new DistributionEnvelope(
            days,
            hours.Count,
            AdvancedReportingService.BuildDistribution(hours, "hours"),
            AdvancedReportingService.CalculatePercentiles(hours)));
    }

    /// <summary>GET /support/admin/reports/agent-ranking — composite-scored agent leaderboard.</summary>
    [HttpGet("agent-ranking")]
    public async Task<IActionResult> AgentRanking([FromQuery] int days = 30, CancellationToken ct = default)
    {
        var (start, end) = Window(days);

        var tickets = await _db.Tickets.AsNoTracking()
            .Where(t => t.CreatedAt >= start && t.CreatedAt <= end && t.AssignedTo != null)
            .ToListAsync(ct);

        var ratings = await RatingsInWindowAsync(start, end, ct);
        var ticketAgent = tickets.ToDictionary(t => t.Id, t => t.AssignedTo!);

        var ranked = tickets
            .GroupBy(t => t.AssignedTo!)
            .Select(g =>
            {
                var total = g.Count();
                var resolved = g.Count(t => t.ResolvedAt != null);
                var resolutionRate = total > 0 ? (double)resolved / total * 100 : 0;

                var frt = HoursDiffs(g, t => t.FirstResponseAt);
                var res = HoursDiffs(g, t => t.ResolvedAt);
                double? avgFrt = frt.Count > 0 ? Math.Round(frt.Average(), 1) : null;
                double? avgResolution = res.Count > 0 ? Math.Round(res.Average(), 1) : null;

                var agentRatings = ratings
                    .Where(r => ticketAgent.TryGetValue(r.TicketId, out var a) && a == g.Key)
                    .ToList();
                double? avgCsat = agentRatings.Count > 0
                    ? Math.Round(agentRatings.Average(r => (double)r.Rating), 1)
                    : null;

                return new
                {
                    AgentId = g.Key,
                    Total = total,
                    Resolved = resolved,
                    ResolutionRate = Math.Round(resolutionRate, 1),
                    AvgFrt = avgFrt,
                    AvgResolution = avgResolution,
                    AvgCsat = avgCsat,
                    Composite = AdvancedReportingService.CompositeScore(
                        resolutionRate, avgFrt, avgResolution, avgCsat),
                };
            })
            .OrderByDescending(x => x.Composite)
            .ThenBy(x => x.AgentId)
            .Select((x, i) => new AgentRankRow(
                x.AgentId,
                x.Total,
                x.Resolved,
                x.ResolutionRate,
                x.AvgFrt,
                x.AvgResolution,
                x.AvgCsat,
                x.Composite,
                i + 1))
            .ToList();

        return Ok(new AgentRankingEnvelope(days, ranked));
    }

    /// <summary>GET /support/admin/reports/sla — SLA compliance rate + recent breaches.</summary>
    [HttpGet("sla")]
    public async Task<IActionResult> Sla([FromQuery] int days = 30, CancellationToken ct = default)
    {
        var (start, end) = Window(days);

        var tickets = await _db.Tickets.AsNoTracking()
            .Where(t => t.CreatedAt >= start && t.CreatedAt <= end)
            .ToListAsync(ct);

        var withPolicy = tickets.Where(t => t.SlaPolicyId != null).ToList();
        var breaches = withPolicy
            .Where(t => t.SlaFirstResponseBreached || t.SlaResolutionBreached)
            .ToList();

        var rows = breaches
            .OrderByDescending(t => t.CreatedAt)
            .Take(100)
            .Select(t => new SlaBreachRow(
                t.Id,
                t.Reference,
                t.Subject,
                t.AssignedTo,
                t.SlaFirstResponseBreached,
                t.SlaResolutionBreached,
                t.CreatedAt))
            .ToList();

        return Ok(new SlaEnvelope(
            days,
            withPolicy.Count,
            breaches.Count,
            SlaComplianceRate(tickets),
            rows));
    }

    /// <summary>GET /support/admin/reports/csat — satisfaction average, response rate, breakdown.</summary>
    [HttpGet("csat")]
    public async Task<IActionResult> Csat([FromQuery] int days = 30, CancellationToken ct = default)
    {
        var (start, end) = Window(days);

        var totalTickets = await _db.Tickets.AsNoTracking()
            .CountAsync(t => t.CreatedAt >= start && t.CreatedAt <= end, ct);

        var ratings = await RatingsInWindowAsync(start, end, ct);

        var breakdown = ratings
            .GroupBy(r => r.Rating)
            .OrderBy(g => g.Key)
            .Select(g => new LabelValue(g.Key.ToString(), g.Count()))
            .ToList();

        return Ok(new CsatEnvelope(
            days,
            ratings.Count == 0 ? 0 : Math.Round(ratings.Average(r => (double)r.Rating), 1),
            totalTickets > 0 ? Math.Round((double)ratings.Count / totalTickets * 100, 1) : 0,
            ratings.Count,
            breakdown));
    }

    /// <summary>GET /support/admin/reports/period-comparison — current vs previous period deltas.</summary>
    [HttpGet("period-comparison")]
    public async Task<IActionResult> PeriodComparison([FromQuery] int days = 30, CancellationToken ct = default)
    {
        var normalized = Math.Clamp(days, 1, 365);
        var now = DateTime.UtcNow;
        var currentStart = now.AddDays(-normalized);
        var previousStart = now.AddDays(-normalized * 2);

        var current = await PeriodMetricsAsync(currentStart, now, ct);
        var previous = await PeriodMetricsAsync(previousStart, currentStart, ct);
        var changes = AdvancedReportingService.CalculateChanges(current, previous);

        return Ok(new PeriodComparisonEnvelope(days, current, previous, changes));
    }

    private async Task<List<SatisfactionRating>> RatingsInWindowAsync(DateTime start, DateTime end, CancellationToken ct)
    {
        return await _db.SatisfactionRatings.AsNoTracking()
            .Where(r => r.CreatedAt >= start && r.CreatedAt <= end)
            .ToListAsync(ct);
    }

    private async Task<Dictionary<string, double>> PeriodMetricsAsync(DateTime start, DateTime end, CancellationToken ct)
    {
        var tickets = await _db.Tickets.AsNoTracking()
            .Where(t => t.CreatedAt >= start && t.CreatedAt < end)
            .ToListAsync(ct);

        var created = tickets.Count;
        var resolved = tickets.Count(t => t.ResolvedAt != null);

        return new Dictionary<string, double>
        {
            ["total_created"] = created,
            ["total_resolved"] = resolved,
            ["resolution_rate"] = created > 0 ? Math.Round((double)resolved / created * 100, 1) : 0,
        };
    }

    private static (DateTime Start, DateTime End) Window(int days)
    {
        var normalized = Math.Clamp(days, 1, 365);
        var end = DateTime.UtcNow;
        return (end.AddDays(-normalized), end);
    }

    private static List<double> HoursDiffs(IEnumerable<Ticket> tickets, Func<Ticket, DateTime?> endSelector)
    {
        return tickets
            .Where(t => endSelector(t) != null)
            .Select(t => Math.Round((endSelector(t)!.Value - t.CreatedAt).TotalHours, 2))
            .Where(h => h >= 0)
            .ToList();
    }

    private static double Average(List<double> values)
    {
        return values.Count == 0 ? 0 : Math.Round(values.Average(), 1);
    }

    private static double SlaComplianceRate(IReadOnlyCollection<Ticket> tickets)
    {
        var withPolicy = tickets.Where(t => t.SlaPolicyId != null).ToList();
        if (withPolicy.Count == 0)
        {
            return 100.0;
        }

        var breached = withPolicy.Count(t => t.SlaFirstResponseBreached || t.SlaResolutionBreached);
        return Math.Round((double)(withPolicy.Count - breached) / withPolicy.Count * 100, 1);
    }

#pragma warning disable CA1034 // Nested types acceptable for grouped API contract records

    public sealed record LabelValue(
        [property: JsonPropertyName("label")] string Label,
        [property: JsonPropertyName("value")] int Value);

    public sealed record SummaryEnvelope(
        [property: JsonPropertyName("period_days")] int PeriodDays,
        [property: JsonPropertyName("total_tickets")] int TotalTickets,
        [property: JsonPropertyName("resolved_tickets")] int ResolvedTickets,
        [property: JsonPropertyName("avg_first_response_hours")] double AvgFirstResponseHours,
        [property: JsonPropertyName("avg_resolution_hours")] double AvgResolutionHours,
        [property: JsonPropertyName("sla_compliance_rate")] double SlaComplianceRate,
        [property: JsonPropertyName("csat_average")] double CsatAverage,
        [property: JsonPropertyName("by_status")] IReadOnlyList<LabelValue> ByStatus,
        [property: JsonPropertyName("by_priority")] IReadOnlyList<LabelValue> ByPriority,
        [property: JsonPropertyName("volume")] IReadOnlyList<LabelValue> Volume);

    public sealed record DistributionEnvelope(
        [property: JsonPropertyName("period_days")] int PeriodDays,
        [property: JsonPropertyName("sample_size")] int SampleSize,
        [property: JsonPropertyName("distribution")] object Distribution,
        [property: JsonPropertyName("percentiles")] IReadOnlyDictionary<string, double> Percentiles);

    public sealed record AgentRankingEnvelope(
        [property: JsonPropertyName("period_days")] int PeriodDays,
        [property: JsonPropertyName("ranking")] IReadOnlyList<AgentRankRow> Ranking);

    public sealed record AgentRankRow(
        [property: JsonPropertyName("agent_id")] string AgentId,
        [property: JsonPropertyName("total_tickets")] int TotalTickets,
        [property: JsonPropertyName("resolved_tickets")] int ResolvedTickets,
        [property: JsonPropertyName("resolution_rate")] double ResolutionRate,
        [property: JsonPropertyName("avg_response_hours")] double? AvgResponseHours,
        [property: JsonPropertyName("avg_resolution_hours")] double? AvgResolutionHours,
        [property: JsonPropertyName("csat_average")] double? CsatAverage,
        [property: JsonPropertyName("composite_score")] double CompositeScore,
        [property: JsonPropertyName("rank")] int Rank);

    public sealed record SlaEnvelope(
        [property: JsonPropertyName("period_days")] int PeriodDays,
        [property: JsonPropertyName("total")] int Total,
        [property: JsonPropertyName("breached")] int Breached,
        [property: JsonPropertyName("compliance_rate")] double ComplianceRate,
        [property: JsonPropertyName("breaches")] IReadOnlyList<SlaBreachRow> Breaches);

    public sealed record SlaBreachRow(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("reference")] string Reference,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("assigned_to")] string? AssignedTo,
        [property: JsonPropertyName("sla_first_response_breached")] bool SlaFirstResponseBreached,
        [property: JsonPropertyName("sla_resolution_breached")] bool SlaResolutionBreached,
        [property: JsonPropertyName("created_at")] DateTime CreatedAt);

    public sealed record CsatEnvelope(
        [property: JsonPropertyName("period_days")] int PeriodDays,
        [property: JsonPropertyName("csat_average")] double CsatAverage,
        [property: JsonPropertyName("response_rate")] double ResponseRate,
        [property: JsonPropertyName("total_ratings")] int TotalRatings,
        [property: JsonPropertyName("breakdown")] IReadOnlyList<LabelValue> Breakdown);

    public sealed record PeriodComparisonEnvelope(
        [property: JsonPropertyName("period_days")] int PeriodDays,
        [property: JsonPropertyName("current")] IReadOnlyDictionary<string, double> Current,
        [property: JsonPropertyName("previous")] IReadOnlyDictionary<string, double> Previous,
        [property: JsonPropertyName("changes")] IReadOnlyDictionary<string, double> Changes);

#pragma warning restore CA1034
}
