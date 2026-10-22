using System.Net;
using System.Text.Json;
using Escalated.Enums;
using Escalated.Models;
using Escalated.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Escalated.Tests.Hosting.InertiaTestSupport;

namespace Escalated.Tests.Hosting;

/// <summary>
/// The report pages against escalated-laravel's <c>ReportController</c> and
/// <c>ReportingService</c>: the overview (<c>Admin/Reports</c>), <c>Dashboard</c>,
/// <c>AgentMetrics</c>, <c>SlaReport</c>, <c>CsatReport</c> and <c>Comparison</c>.
/// </summary>
public class ReportPagesTests
{
    private const string Admin = "admin-1";
    private const string Avery = "agent-1";
    private const string Blake = "agent-2";

    private sealed class Directory : IUserDirectory
    {
        private static readonly Dictionary<string, UserDirectoryEntry> Users = new()
        {
            [Avery] = new(Avery, "Avery Agent", "avery@example.com"),
            [Blake] = new(Blake, "Blake Agent", "blake@example.com"),
        };

        public Task<UserDirectoryPage> ListAsync(string? search, int page, int pageSize, CancellationToken ct = default) =>
            Task.FromResult(new UserDirectoryPage(Users.Values.ToList(), Users.Count, page, pageSize));

        public Task<UserDirectoryEntry?> FindAsync(string id, CancellationToken ct = default) =>
            Task.FromResult(Users.GetValueOrDefault(id));
    }

    private static async Task<(EscalatedTestHost Host, HttpClient Client)> StartAsync()
    {
        var host = await EscalatedTestHost.StartAsync(
            options => options.Ui.Enabled = true,
            services => services.AddSingleton<IUserDirectory, Directory>());
        await host.GrantRoleAsync(Admin, "escalated-admin");

        var now = DateTime.UtcNow;
        await host.SeedAsync(async db =>
        {
            var standard = new SlaPolicy { Name = "Standard", IsActive = true };
            db.SlaPolicies.Add(standard);
            await db.SaveChangesAsync();

            // In the last 30 days: four tickets.
            var a = new Ticket
            {
                Reference = "ESC-00001",
                Subject = "Resolved fast",
                Status = TicketStatus.Resolved,
                Priority = TicketPriority.High,
                AssignedTo = Avery,
                SlaPolicyId = standard.Id,
                CreatedAt = now.AddDays(-2),
                FirstResponseAt = now.AddDays(-2).AddHours(2),
                ResolvedAt = now.AddDays(-2).AddHours(10),
            };
            var b = new Ticket
            {
                Reference = "ESC-00002",
                Subject = "Breached",
                Status = TicketStatus.Open,
                Priority = TicketPriority.High,
                AssignedTo = Avery,
                SlaPolicyId = standard.Id,
                SlaFirstResponseBreached = true,
                CreatedAt = now.AddDays(-2),
                FirstResponseAt = now.AddDays(-2).AddHours(4),
            };
            var c = new Ticket
            {
                Reference = "ESC-00003",
                Subject = "Blake's",
                Status = TicketStatus.Closed,
                Priority = TicketPriority.Low,
                AssignedTo = Blake,
                CreatedAt = now.AddDays(-1),
                ResolvedAt = now.AddDays(-1).AddHours(20),
            };
            var d = new Ticket { Reference = "ESC-00004", Subject = "Unassigned", Priority = TicketPriority.Low, CreatedAt = now };
            // Previous period (31-60 days ago): one breached ticket.
            var old = new Ticket
            {
                Reference = "ESC-00005",
                Subject = "Old",
                Status = TicketStatus.Closed,
                Priority = TicketPriority.Medium,
                SlaPolicyId = standard.Id,
                SlaResolutionBreached = true,
                CreatedAt = now.AddDays(-40),
                ResolvedAt = now.AddDays(-39),
            };
            db.Tickets.AddRange(a, b, c, d, old);
            await db.SaveChangesAsync();

            db.SatisfactionRatings.AddRange(
                new SatisfactionRating { TicketId = a.Id, Rating = 5, CreatedAt = now.AddDays(-1) },
                new SatisfactionRating { TicketId = c.Id, Rating = 2, CreatedAt = now },
                new SatisfactionRating { TicketId = old.Id, Rating = 4, CreatedAt = now.AddDays(-38) });
            await db.SaveChangesAsync();
        });

        return (host, host.ClientFor(Admin));
    }

    [Fact]
    public async Task Overview_SummarisesThePeriod()
    {
        var (host, client) = await StartAsync();
        await using var __ = host;

        var page = await PageAsync(client, "/support/admin/reports");
        AssertPage(page, "Escalated/Admin/Reports");
        var props = page.GetProperty("props");
        Assert.Equal(30, props.GetProperty("period_days").GetInt32());
        Assert.Equal(4, props.GetProperty("total_tickets").GetInt32());
        Assert.Equal(2, props.GetProperty("resolved_tickets").GetInt32());
        Assert.Equal(3, props.GetProperty("avg_first_response_hours").GetDouble());
        Assert.Equal(1, props.GetProperty("sla_breach_count").GetInt32());
        Assert.Equal(2, props.GetProperty("by_status").GetProperty("open").GetInt32());
        Assert.Equal(2, props.GetProperty("by_priority").GetProperty("low").GetInt32());
        var csat = Keys(props.GetProperty("csat"), "average", "total", "breakdown");
        Assert.Equal(3.5, csat.GetProperty("average").GetDouble());
        Assert.Equal(2, csat.GetProperty("total").GetInt32());
        Assert.Equal(1, csat.GetProperty("breakdown").GetProperty("5").GetInt32());

        var week = await PageAsync(client, "/support/admin/reports?days=90");
        Assert.Equal(5, week.GetProperty("props").GetProperty("total_tickets").GetInt32());
    }

    [Fact]
    public async Task Dashboard_RendersTheChartsKpisAndAgents()
    {
        var (host, client) = await StartAsync();
        await using var __ = host;

        var page = await PageAsync(client, "/support/admin/reports/dashboard");
        AssertPage(page, "Escalated/Admin/Reports/Dashboard");
        var props = page.GetProperty("props");
        var volume = props.GetProperty("volume").EnumerateArray().ToList();
        Assert.Equal(3, volume.Count);
        Keys(volume[0], "label", "value");
        Assert.Equal(2, volume[0].GetProperty("value").GetInt32());
        Assert.Equal(5, props.GetProperty("by_status").EnumerateArray().Sum(s => Keys(s, "label", "value").GetProperty("value").GetInt32()));
        Assert.Equal(5, props.GetProperty("by_priority").EnumerateArray().Sum(s => s.GetProperty("value").GetInt32()));
        Assert.Equal(3, props.GetProperty("avg_response_hours").GetDouble());
        Assert.Equal(15, props.GetProperty("avg_resolution_hours").GetDouble());
        Assert.Equal(50, props.GetProperty("sla_compliance").GetDouble());
        Assert.Equal(3.5, props.GetProperty("csat_average").GetDouble());

        var agents = props.GetProperty("agent_performance").EnumerateArray().ToList();
        var avery = Keys(agents.Single(a => a.GetProperty("agent_id").GetString() == Avery),
            "agent_id", "agent_name", "total_tickets", "resolved_tickets", "avg_response_hours", "avg_resolution_hours");
        Assert.Equal("Avery Agent", avery.GetProperty("agent_name").GetString());
        Assert.Equal(2, avery.GetProperty("total_tickets").GetInt32());
        Assert.Equal(1, avery.GetProperty("resolved_tickets").GetInt32());
        Assert.Equal(3, avery.GetProperty("avg_response_hours").GetDouble());
        Assert.Equal(JsonValueKind.Null, agents.Single(a => a.GetProperty("agent_id").GetString() == Blake).GetProperty("avg_response_hours").ValueKind);
    }

    [Fact]
    public async Task AgentMetrics_ListsEachAgent()
    {
        var (host, client) = await StartAsync();
        await using var __ = host;

        var page = await PageAsync(client, "/support/admin/reports/agents?days=7");
        AssertPage(page, "Escalated/Admin/Reports/AgentMetrics");
        Assert.Equal(7, page.GetProperty("props").GetProperty("period_days").GetInt32());
        Assert.Equal(2, page.GetProperty("props").GetProperty("agents").GetArrayLength());
    }

    [Fact]
    public async Task SlaReport_ShowsComplianceByPolicyAndTheBreaches()
    {
        var (host, client) = await StartAsync();
        await using var __ = host;

        var page = await PageAsync(client, "/support/admin/reports/sla");
        AssertPage(page, "Escalated/Admin/Reports/SlaReport");
        var props = page.GetProperty("props");
        Assert.Equal(50, props.GetProperty("compliance_rate").GetDouble());
        var policy = Keys(Assert.Single(props.GetProperty("compliance_by_policy").EnumerateArray()), "policy_name", "total", "met", "breached");
        Assert.Equal(2, policy.GetProperty("total").GetInt32());
        Assert.Equal(1, policy.GetProperty("breached").GetInt32());
        var breach = Keys(Assert.Single(props.GetProperty("breaches").EnumerateArray()), "id", "reference", "subject",
            "sla_first_response_breached", "sla_resolution_breached", "created_at", "sla_policy", "assignee");
        Assert.Equal("ESC-00002", breach.GetProperty("reference").GetString());
        Assert.Equal("Standard", breach.GetProperty("sla_policy").GetProperty("name").GetString());
    }

    [Fact]
    public async Task CsatReport_ShowsTheAverageResponseRateAgentsAndTrend()
    {
        var (host, client) = await StartAsync();
        await using var __ = host;

        var page = await PageAsync(client, "/support/admin/reports/csat");
        AssertPage(page, "Escalated/Admin/Reports/CsatReport");
        var props = page.GetProperty("props");
        Assert.Equal(3.5, props.GetProperty("csat_average").GetDouble());
        Assert.Equal(50, props.GetProperty("response_rate").GetDouble());
        Assert.Equal(2, props.GetProperty("total_ratings").GetInt32());
        var blake = Keys(props.GetProperty("by_agent").EnumerateArray().Single(a => a.GetProperty("agent_id").GetString() == Blake),
            "agent_id", "agent_name", "avg_rating", "total_ratings");
        Assert.Equal(2, blake.GetProperty("avg_rating").GetDouble());
        Assert.Equal(2, props.GetProperty("over_time").GetArrayLength());
        Keys(props.GetProperty("over_time")[0], "label", "value");
    }

    [Fact]
    public async Task Comparison_PutsTheTwoPeriodsSideBySide()
    {
        var (host, client) = await StartAsync();
        await using var __ = host;

        var page = await PageAsync(client, "/support/admin/reports/comparison");
        AssertPage(page, "Escalated/Admin/Reports/Comparison");
        var props = page.GetProperty("props");
        string[] keys = { "total_tickets", "resolved_tickets", "avg_frt", "avg_resolution", "sla_compliance", "csat", "breach_count", "volume_trend" };
        var current = Keys(props.GetProperty("current"), keys);
        var previous = Keys(props.GetProperty("previous"), keys);
        Assert.Equal(4, current.GetProperty("total_tickets").GetInt32());
        Assert.Equal(1, previous.GetProperty("total_tickets").GetInt32());
        Assert.Equal(1, previous.GetProperty("breach_count").GetInt32());
        Assert.Equal(0, previous.GetProperty("sla_compliance").GetDouble());
        Assert.Equal(4, previous.GetProperty("csat").GetDouble());
        Assert.Single(previous.GetProperty("volume_trend").EnumerateArray());
    }

    [Theory]
    [InlineData("/support/admin/reports/response-times")]
    [InlineData("/support/admin/reports/frt")]
    public async Task ResponseTimes_SummariseFirstResponses(string url)
    {
        var (host, client) = await StartAsync();
        await using var __ = host;

        var page = await PageAsync(client, url);
        AssertPage(page, "Escalated/Admin/Reports/ResponseTimes");
        var props = page.GetProperty("props");
        Assert.Equal(3, props.GetProperty("avg_frt").GetDouble());
        Assert.Equal(3, props.GetProperty("median_frt").GetDouble());
        Assert.Equal(3.8, props.GetProperty("p90_frt").GetDouble());
        Assert.Equal(100, props.GetProperty("pct_under_target").GetDouble());
        Assert.Equal(4, props.GetProperty("target_hours").GetDouble());
        var buckets = props.GetProperty("distribution").EnumerateArray().ToList();
        Assert.Equal(new[] { "<1h", "1-4h", "4-8h", "8-24h", ">24h" }, buckets.Select(b => b.GetProperty("label").GetString()));
        Assert.Equal(new[] { 0, 1, 1, 0, 0 }, buckets.Select(b => b.GetProperty("value").GetInt32()));
        var trend = Assert.Single(props.GetProperty("trend").EnumerateArray());
        Assert.Equal(3, Keys(trend, "label", "value").GetProperty("value").GetDouble());
        var agent = Keys(Assert.Single(props.GetProperty("by_agent").EnumerateArray()), "agent_id", "agent_name", "count", "avg", "median", "p90");
        Assert.Equal("Avery Agent", agent.GetProperty("agent_name").GetString());
        Assert.Equal("Unassigned", Assert.Single(props.GetProperty("by_department").EnumerateArray()).GetProperty("label").GetString());
        Assert.Equal("high", Assert.Single(props.GetProperty("by_priority").EnumerateArray()).GetProperty("label").GetString());
    }

    [Fact]
    public async Task ResolutionTimes_SummariseResolutions()
    {
        var (host, client) = await StartAsync();
        await using var __ = host;

        var page = await PageAsync(client, "/support/admin/reports/resolution-times");
        AssertPage(page, "Escalated/Admin/Reports/ResolutionTimes");
        var props = page.GetProperty("props");
        Assert.Equal(15, props.GetProperty("avg_resolution").GetDouble());
        Assert.Equal(15, props.GetProperty("median_resolution").GetDouble());
        Assert.Equal(19, props.GetProperty("p90_resolution").GetDouble());
        Assert.Equal(100, props.GetProperty("pct_under_target").GetDouble());
        Assert.Equal(24, props.GetProperty("target_hours").GetDouble());
        Assert.Equal(6, props.GetProperty("distribution").GetArrayLength());
        Assert.Equal(2, props.GetProperty("by_agent").GetArrayLength());
        var channel = Assert.Single(props.GetProperty("by_channel").EnumerateArray());
        Assert.Equal("unknown", channel.GetProperty("label").GetString());
        Assert.Equal(15, channel.GetProperty("value").GetDouble());
    }

    [Fact]
    public async Task AgentRanking_ScoresAndOrdersTheAgents()
    {
        var (host, client) = await StartAsync();
        await using var __ = host;

        var page = await PageAsync(client, "/support/admin/reports/agent-ranking");
        AssertPage(page, "Escalated/Admin/Reports/AgentRanking");
        var agents = page.GetProperty("props").GetProperty("agents").EnumerateArray().ToList();
        Assert.Equal(new[] { Avery, Blake }, agents.Select(a => a.GetProperty("agent_id").GetString()));
        var avery = Keys(agents[0], "agent_id", "agent_name", "volume", "resolution_rate", "avg_frt", "avg_resolution", "csat", "composite_score");
        Assert.Equal(2, avery.GetProperty("volume").GetInt32());
        Assert.Equal(50, avery.GetProperty("resolution_rate").GetDouble());
        Assert.Equal(5, avery.GetProperty("csat").GetDouble());
        Assert.Equal(52.9, agents[1].GetProperty("composite_score").GetDouble());
    }

    [Fact]
    public async Task SlaTrends_ChartBreachesAndListTicketsAtRisk()
    {
        var (host, client) = await StartAsync();
        await using var __ = host;
        await host.SeedAsync(async db =>
        {
            db.Tickets.Add(new Ticket { Reference = "ESC-00006", Subject = "Due soon", Priority = TicketPriority.Urgent, FirstResponseDueAt = DateTime.UtcNow.AddHours(2) });
            await db.SaveChangesAsync();
        });

        var page = await PageAsync(client, "/support/admin/reports/sla-trends");
        AssertPage(page, "Escalated/Admin/Reports/SlaTrends");
        var props = page.GetProperty("props");
        Assert.Equal(1, props.GetProperty("total_breaches").GetInt32());
        Assert.Equal(20, props.GetProperty("breach_rate").GetDouble());
        Assert.Equal(1, props.GetProperty("first_response_breaches").GetInt32());
        Assert.Equal(0, props.GetProperty("resolution_breaches").GetInt32());
        Assert.Equal(1, Keys(Assert.Single(props.GetProperty("breach_trend").EnumerateArray()), "label", "value").GetProperty("value").GetInt32());
        var byType = Keys(Assert.Single(props.GetProperty("breach_by_type_trend").EnumerateArray()), "label", "values");
        Assert.Equal(new[] { 1, 0 }, byType.GetProperty("values").EnumerateArray().Select(v => v.GetInt32()));
        var department = Assert.Single(props.GetProperty("breach_by_department").EnumerateArray());
        Assert.Equal("Unassigned", department.GetProperty("label").GetString());
        Assert.Equal(50, department.GetProperty("value").GetDouble());
        Assert.Equal(50, Assert.Single(props.GetProperty("breach_by_priority").EnumerateArray()).GetProperty("value").GetDouble());
        var risk = Keys(Assert.Single(props.GetProperty("at_risk_tickets").EnumerateArray()), "id", "reference", "subject", "hours_remaining");
        Assert.Equal("ESC-00006", risk.GetProperty("reference").GetString());
        Assert.InRange(risk.GetProperty("hours_remaining").GetDouble(), 1.9, 2.0);

        var monthly = await PageAsync(client, "/support/admin/reports/sla-trends?group_by=month");
        Assert.Matches(@"^\d{4}-\d{2}$", monthly.GetProperty("props").GetProperty("breach_trend")[0].GetProperty("label").GetString()!);
    }

    [Fact]
    public async Task Cohorts_GroupTicketsFiveWays()
    {
        var (host, client) = await StartAsync();
        await using var __ = host;
        await host.SeedAsync(async db =>
        {
            var tag = new Tag { Name = "VIP", Slug = "vip" };
            db.Tags.Add(tag);
            await db.SaveChangesAsync();
            db.TicketTags.Add(new TicketTag { TicketId = await db.Tickets.Where(t => t.Reference == "ESC-00002").Select(t => t.Id).SingleAsync(), TagId = tag.Id });
            await db.SaveChangesAsync();
        });

        var page = await PageAsync(client, "/support/admin/reports/cohorts");
        AssertPage(page, "Escalated/Admin/Reports/Cohorts");
        var props = page.GetProperty("props");
        var tag = Keys(Assert.Single(props.GetProperty("by_tag").EnumerateArray()), "name", "volume", "avg_resolution", "breach_rate", "csat");
        Assert.Equal("VIP", tag.GetProperty("name").GetString());
        Assert.Equal(100, tag.GetProperty("breach_rate").GetDouble());
        var department = Assert.Single(props.GetProperty("by_department").EnumerateArray());
        Assert.Equal("Unassigned", department.GetProperty("name").GetString());
        Assert.Equal(4, department.GetProperty("volume").GetInt32());
        Assert.Equal(15, department.GetProperty("avg_resolution").GetDouble());
        Assert.Equal(25, department.GetProperty("breach_rate").GetDouble());
        Assert.Equal(4, props.GetProperty("by_channel").EnumerateArray().Sum(c => c.GetProperty("volume").GetInt32()));
        Assert.Equal(4, props.GetProperty("by_type").EnumerateArray().Sum(c => c.GetProperty("volume").GetInt32()));
        var high = props.GetProperty("by_priority").EnumerateArray().Single(c => c.GetProperty("name").GetString() == "high");
        Assert.Equal(10, high.GetProperty("avg_resolution").GetDouble());
        Assert.Equal(50, high.GetProperty("breach_rate").GetDouble());
    }

    [Theory]
    [InlineData("/support/admin/reports")]
    [InlineData("/support/admin/reports/sla")]
    [InlineData("/support/admin/reports/csat")]
    public async Task TheJsonApi_StillAnswersApiClients(string url)
    {
        var (host, client) = await StartAsync();
        await using var __ = host;

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("X-Inertia"));
        Assert.Equal(JsonValueKind.Object, JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.ValueKind);
    }

    [Fact]
    public async Task AgentDetail_ShowsOneAgentAgainstThePreviousPeriod()
    {
        var (host, client) = await StartAsync();
        await using var __ = host;
        await host.SeedAsync(async db =>
        {
            var ticket = await db.Tickets.FirstAsync(t => t.Reference == "ESC-00002");
            db.Replies.Add(new Reply { TicketId = ticket.Id, Body = "On it", Type = "reply", AuthorId = Avery, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        });

        var page = await PageAsync(client, $"/support/admin/reports/agent/{Avery}/detail?days=30");
        AssertPage(page, "Escalated/Admin/Reports/AgentDetail");
        var props = page.GetProperty("props");
        Assert.Equal(30, props.GetProperty("period_days").GetInt32());
        var agent = Keys(props.GetProperty("agent"), "id", "name", "email");
        Assert.Equal(Avery, agent.GetProperty("id").GetString());
        Assert.Equal("Avery Agent", agent.GetProperty("name").GetString());
        Assert.Equal("avery@example.com", agent.GetProperty("email").GetString());
        Assert.Equal(2, props.GetProperty("total_tickets").GetInt32());
        Assert.Equal(1, props.GetProperty("resolved_tickets").GetInt32());
        Assert.Equal(3, props.GetProperty("avg_frt").GetDouble());
        Assert.Equal(10, props.GetProperty("avg_resolution").GetDouble());
        Assert.Equal(5, props.GetProperty("csat_score").GetDouble());

        var percentiles = Keys(props.GetProperty("response_percentiles"), "p50", "p75", "p90", "p95", "p99");
        Assert.Equal(3, percentiles.GetProperty("p50").GetDouble());
        Assert.Equal(3.8, percentiles.GetProperty("p90").GetDouble());

        var breakdown = Assert.Single(props.GetProperty("csat_breakdown").EnumerateArray());
        Assert.Equal("5 stars", Keys(breakdown, "label", "value").GetProperty("label").GetString());
        Assert.Equal(2, props.GetProperty("workload").EnumerateArray().Sum(w => Keys(w, "label", "value").GetProperty("value").GetInt32()));

        // One reply written and one ticket resolved, per day.
        var productivity = props.GetProperty("productivity").EnumerateArray().ToList();
        Assert.Equal(1, productivity.Sum(p => Keys(p, "label", "value", "values").GetProperty("values")[0].GetInt32()));
        Assert.Equal(1, productivity.Sum(p => p.GetProperty("values")[1].GetInt32()));
        Assert.All(productivity, p => Assert.Equal(p.GetProperty("values")[0].GetInt32() + p.GetProperty("values")[1].GetInt32(), p.GetProperty("value").GetInt32()));

        Assert.Equal(0, props.GetProperty("prev_total_tickets").GetInt32());
        Assert.Equal(0, props.GetProperty("prev_resolved_tickets").GetInt32());
        Assert.Equal(0, props.GetProperty("prev_avg_frt").GetDouble());
        Assert.Equal(0, props.GetProperty("prev_avg_resolution").GetDouble());
        Assert.Equal(JsonValueKind.Null, props.GetProperty("prev_csat_score").ValueKind);

        var unknown = await PageAsync(client, "/support/admin/reports/agent/nobody/detail");
        Assert.Equal("nobody", unknown.GetProperty("props").GetProperty("agent").GetProperty("name").GetString());
        Assert.Equal(0, unknown.GetProperty("props").GetProperty("total_tickets").GetInt32());
    }
}
