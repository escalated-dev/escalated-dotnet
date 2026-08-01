using Escalated.Controllers.Admin;
using Escalated.Data;
using Escalated.Enums;
using Escalated.Models;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Escalated.Tests.Controllers;

public class AdminReportControllerTests
{
    private const string FastAgent = "10";
    private const string SlowAgent = "11";

    private static readonly DateTime Now = DateTime.UtcNow;

    private static async Task<(AdminReportController Ctrl, EscalatedDbContext Db)> SeedControllerAsync()
    {
        var db = TestHelpers.CreateInMemoryDb();
        await SeedScenarioAsync(db);
        return (new AdminReportController(db), db);
    }

    /// <summary>
    /// Seeds a period of tickets split across a fast, high-CSAT agent and a
    /// slow, low-CSAT agent, plus SLA policies and breaches, so every report
    /// endpoint has populated data to compute over.
    /// </summary>
    private static async Task SeedScenarioAsync(EscalatedDbContext db)
    {
        var policy = new SlaPolicy { Name = "Standard", IsActive = true, CreatedAt = Now, UpdatedAt = Now };
        db.SlaPolicies.Add(policy);
        await db.SaveChangesAsync();

        // Fast agent: 3 tickets, all resolved quickly, no breaches, top CSAT.
        for (var i = 0; i < 3; i++)
        {
            var created = Now.AddDays(-(i + 2));
            var ticket = new Ticket
            {
                Reference = $"ESC-F{i:D3}",
                Subject = $"Fast {i}",
                Status = TicketStatus.Resolved,
                Priority = TicketPriority.High,
                AssignedTo = FastAgent,
                SlaPolicyId = policy.Id,
                FirstResponseAt = created.AddHours(1),
                ResolvedAt = created.AddHours(4),
                CreatedAt = created,
                UpdatedAt = created,
            };
            db.Tickets.Add(ticket);
            await db.SaveChangesAsync();
            db.SatisfactionRatings.Add(new SatisfactionRating
            {
                TicketId = ticket.Id,
                Rating = 5,
                CreatedAt = created.AddHours(5),
            });
        }

        // Slow agent: 3 tickets, mostly open, SLA breaches, low CSAT. Aged
        // enough (5-7 days) that the +60h resolution / +61h rating stamps stay
        // in the past and inside the reporting window.
        for (var i = 0; i < 3; i++)
        {
            var created = Now.AddDays(-(i + 5));
            var resolved = i == 0; // only the first is resolved (and slowly)
            var ticket = new Ticket
            {
                Reference = $"ESC-S{i:D3}",
                Subject = $"Slow {i}",
                Status = resolved ? TicketStatus.Resolved : TicketStatus.Open,
                Priority = TicketPriority.Medium,
                AssignedTo = SlowAgent,
                SlaPolicyId = policy.Id,
                FirstResponseAt = created.AddHours(20),
                ResolvedAt = resolved ? created.AddHours(60) : null,
                SlaFirstResponseBreached = true,
                SlaResolutionBreached = resolved,
                CreatedAt = created,
                UpdatedAt = created,
            };
            db.Tickets.Add(ticket);
            await db.SaveChangesAsync();
            db.SatisfactionRatings.Add(new SatisfactionRating
            {
                TicketId = ticket.Id,
                Rating = 2,
                CreatedAt = created.AddHours(61),
            });
        }

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Index_ReturnsPopulatedSummary()
    {
        var (ctrl, _) = await SeedControllerAsync();

        var ok = Assert.IsType<OkObjectResult>(await ctrl.Index());
        var body = Assert.IsType<AdminReportController.SummaryEnvelope>(ok.Value);

        Assert.Equal(30, body.PeriodDays);
        Assert.Equal(6, body.TotalTickets);
        Assert.Equal(4, body.ResolvedTickets);
        Assert.True(body.AvgFirstResponseHours > 0);
        Assert.True(body.AvgResolutionHours > 0);
        Assert.NotEmpty(body.ByStatus);
        Assert.NotEmpty(body.ByPriority);
        Assert.NotEmpty(body.Volume);
        Assert.True(body.CsatAverage > 0);
        // Half the SLA tickets breached (3 of 6), so compliance sits below 100.
        Assert.True(body.SlaComplianceRate < 100);
    }

    [Fact]
    public async Task Index_EmptyDatabase_ReturnsZeroedSummaryWithFullCompliance()
    {
        var ctrl = new AdminReportController(TestHelpers.CreateInMemoryDb());

        var ok = Assert.IsType<OkObjectResult>(await ctrl.Index());
        var body = Assert.IsType<AdminReportController.SummaryEnvelope>(ok.Value);

        Assert.Equal(0, body.TotalTickets);
        Assert.Equal(0, body.ResolvedTickets);
        Assert.Equal(0, body.CsatAverage);
        Assert.Equal(100.0, body.SlaComplianceRate);
    }

    [Fact]
    public async Task FirstResponseTime_ReturnsDistributionAndPercentiles()
    {
        var (ctrl, _) = await SeedControllerAsync();

        var ok = Assert.IsType<OkObjectResult>(await ctrl.FirstResponseTime());
        var body = Assert.IsType<AdminReportController.DistributionEnvelope>(ok.Value);

        Assert.Equal(6, body.SampleSize);
        Assert.NotNull(body.Distribution);
        Assert.True(body.Percentiles["p50"] > 0);
        Assert.True(body.Percentiles.ContainsKey("p90"));
        Assert.True(body.Percentiles.ContainsKey("p99"));
    }

    [Fact]
    public async Task ResolutionTime_ReturnsDistributionAndPercentiles()
    {
        var (ctrl, _) = await SeedControllerAsync();

        var ok = Assert.IsType<OkObjectResult>(await ctrl.ResolutionTime());
        var body = Assert.IsType<AdminReportController.DistributionEnvelope>(ok.Value);

        // 4 resolved tickets across both agents.
        Assert.Equal(4, body.SampleSize);
        Assert.True(body.Percentiles["p50"] > 0);
    }

    [Fact]
    public async Task AgentRanking_RanksAgentsByCompositeScoreDescending()
    {
        var (ctrl, _) = await SeedControllerAsync();

        var ok = Assert.IsType<OkObjectResult>(await ctrl.AgentRanking());
        var body = Assert.IsType<AdminReportController.AgentRankingEnvelope>(ok.Value);

        Assert.Equal(2, body.Ranking.Count);

        var first = body.Ranking[0];
        var second = body.Ranking[1];

        Assert.Equal(1, first.Rank);
        Assert.Equal(2, second.Rank);
        // The fast, high-CSAT, fully-resolving agent must outrank the slow one.
        Assert.Equal(FastAgent, first.AgentId);
        Assert.Equal(SlowAgent, second.AgentId);
        Assert.True(first.CompositeScore > second.CompositeScore);
        Assert.Equal(100.0, first.ResolutionRate);
        Assert.Equal(5.0, first.CsatAverage);
    }

    [Fact]
    public async Task Sla_ReturnsComplianceRateAndBreachRows()
    {
        var (ctrl, _) = await SeedControllerAsync();

        var ok = Assert.IsType<OkObjectResult>(await ctrl.Sla());
        var body = Assert.IsType<AdminReportController.SlaEnvelope>(ok.Value);

        Assert.Equal(6, body.Total); // all tickets carry a policy
        Assert.Equal(3, body.Breached); // the three slow-agent tickets breached
        Assert.True(body.ComplianceRate < 100);
        Assert.NotEmpty(body.Breaches);
        Assert.All(body.Breaches, b => Assert.True(b.SlaFirstResponseBreached || b.SlaResolutionBreached));
    }

    [Fact]
    public async Task Csat_ReturnsAverageResponseRateAndBreakdown()
    {
        var (ctrl, _) = await SeedControllerAsync();

        var ok = Assert.IsType<OkObjectResult>(await ctrl.Csat());
        var body = Assert.IsType<AdminReportController.CsatEnvelope>(ok.Value);

        Assert.Equal(6, body.TotalRatings);
        Assert.True(body.CsatAverage > 0);
        // 6 ratings over 6 tickets in the window.
        Assert.Equal(100.0, body.ResponseRate);
        Assert.NotEmpty(body.Breakdown);
        Assert.Contains(body.Breakdown, b => b.Label == "5");
        Assert.Contains(body.Breakdown, b => b.Label == "2");
    }

    [Fact]
    public async Task PeriodComparison_ReturnsCurrentPreviousAndChanges()
    {
        var db = TestHelpers.CreateInMemoryDb();
        // 4 tickets in the current 30-day window.
        for (var i = 0; i < 4; i++)
        {
            db.Tickets.Add(new Ticket
            {
                Reference = $"ESC-C{i:D3}",
                Subject = $"Current {i}",
                CreatedAt = Now.AddDays(-3),
                UpdatedAt = Now.AddDays(-3),
            });
        }

        // 2 tickets in the previous window (31-60 days ago).
        for (var i = 0; i < 2; i++)
        {
            db.Tickets.Add(new Ticket
            {
                Reference = $"ESC-P{i:D3}",
                Subject = $"Previous {i}",
                CreatedAt = Now.AddDays(-40),
                UpdatedAt = Now.AddDays(-40),
            });
        }

        await db.SaveChangesAsync();
        var ctrl = new AdminReportController(db);

        var ok = Assert.IsType<OkObjectResult>(await ctrl.PeriodComparison());
        var body = Assert.IsType<AdminReportController.PeriodComparisonEnvelope>(ok.Value);

        Assert.Equal(4, body.Current["total_created"]);
        Assert.Equal(2, body.Previous["total_created"]);
        // 4 vs 2 => +100% change.
        Assert.Equal(100, body.Changes["total_created"]);
        Assert.True(body.Changes.ContainsKey("resolution_rate"));
    }

    [Fact]
    public void Reports_AreGatedUnderTheAdminRoutePrefix()
    {
        var type = typeof(AdminReportController);

        var api = type.GetCustomAttributes(typeof(ApiControllerAttribute), false);
        Assert.Single(api);

        var route = Assert.Single(type.GetCustomAttributes(typeof(RouteAttribute), false));
        var template = ((RouteAttribute)route).Template;
        Assert.Equal("support/admin/reports", template);
        // The host's admin gate protects everything under support/admin/*.
        Assert.StartsWith("support/admin", template, StringComparison.Ordinal);
    }
}
