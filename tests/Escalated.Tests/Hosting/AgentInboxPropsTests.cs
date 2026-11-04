using System.Net;
using System.Text.Json;
using Escalated.Enums;
using Escalated.Models;
using Escalated.Ui;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Escalated.Tests.Hosting.InertiaTestSupport;

namespace Escalated.Tests.Hosting;

/// <summary>
/// The agent panel's optional <c>inbox</c> and <c>queue</c> props, and the list
/// filters its rail, views and stat strip link to (shared frontend
/// docs/agent-panel.md, after escalated-laravel's AgentInboxPropsTest). Every
/// number must be what the agent would get by opening the list it labels.
/// </summary>
public class AgentInboxPropsTests
{
    private const string Agent = "agent-1";
    private const string Other = "agent-2";
    private const string Index = "/support/agent/tickets";

    private static readonly string[] BuiltInViews = ["urgent", "sla_at_risk", "waiting_on_me", "waiting_on_customer", "recently_updated"];

    private static int _reference;

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static async Task<(EscalatedTestHost Host, HttpClient Client)> StartAsync(
        Action<Escalated.Configuration.EscalatedOptions>? configure = null, Action<IServiceCollection>? services = null)
    {
        var host = await EscalatedTestHost.StartAsync(options =>
        {
            options.Ui.Enabled = true;
            options.Ui.AgentInboxCacheSeconds = 0;
            configure?.Invoke(options);
        }, services);
        await host.GrantRoleAsync(Agent, "escalated-agent");
        await host.GrantRoleAsync(Other, "escalated-agent");
        return (host, host.ClientFor(Agent));
    }

    private static Ticket T(Action<Ticket>? customise = null)
    {
        var ticket = new Ticket
        {
            Reference = $"ESC-{Interlocked.Increment(ref _reference) + 1000}",
            Subject = "Ticket",
            Priority = TicketPriority.Low,
            Status = TicketStatus.Open,
        };
        customise?.Invoke(ticket);
        return ticket;
    }

    private static Task SeedAsync(EscalatedTestHost host, params Ticket[] tickets) => host.SeedAsync(async db =>
    {
        db.Tickets.AddRange(tickets);
        await db.SaveChangesAsync();
    });

    private static Action<Ticket> Resolved(DateTime at, string? assignee = null) => t =>
    {
        t.Status = TicketStatus.Resolved;
        t.ResolvedAt = at;
        t.AssignedTo = assignee;
    };

    private static async Task<JsonElement> PropsAsync(HttpClient client, string url) =>
        (await PageAsync(client, url)).GetProperty("props");

    private static void Json(string expected, JsonElement actual) =>
        Assert.Equal(Normal(JsonDocument.Parse(expected).RootElement), Normal(actual));

    /// <summary>The JSON with object keys sorted, so two objects compare regardless of key order.</summary>
    private static string Normal(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => "{" + string.Join(",", element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => JsonSerializer.Serialize(p.Name) + ":" + Normal(p.Value))) + "}",
        JsonValueKind.Array => "[" + string.Join(",", element.EnumerateArray().Select(Normal)) + "]",
        _ => element.GetRawText(),
    };

    private static List<int> Ids(JsonElement props) =>
        props.GetProperty("tickets").GetProperty("data").EnumerateArray().Select(r => r.GetProperty("id").GetInt32()).ToList();

    private static JsonElement View(JsonElement inbox, string id) =>
        inbox.GetProperty("views").EnumerateArray().Single(v => v.GetProperty("id").ToString() == id);

    [Fact]
    public async Task TheListGetsCountsBuiltInViewsStatsAndTheQueue()
    {
        var (host, client) = await StartAsync();
        await using var __ = host;
        var now = DateTime.UtcNow;
        await SeedAsync(host,
            T(t => { t.AssignedTo = Agent; t.Priority = TicketPriority.Urgent; }),
            T(t => { t.AssignedTo = Agent; t.Status = TicketStatus.WaitingOnCustomer; }),
            T(Resolved(now, Agent)),
            T(Resolved(now.AddDays(-2), Agent)),
            T(t => { t.AssignedTo = Agent; t.SnoozedUntil = now.AddDays(1); }),
            T(),
            T(t => { t.Status = TicketStatus.Closed; t.ClosedAt = now; }),
            T(t => t.AssignedTo = Other));

        var page = await PageAsync(client, Index);
        AssertPage(page, "Escalated/Agent/TicketIndex");
        var props = page.GetProperty("props");
        var inbox = props.GetProperty("inbox");

        Assert.Equal(["counts", "views", "stats"], inbox.EnumerateObject().Select(p => p.Name));
        // Unresolved, unsnoozed tickets in each queue.
        Json("""{"mine":2,"unassigned":1,"all":4}""", inbox.GetProperty("counts"));
        Assert.Equal(BuiltInViews, inbox.GetProperty("views").EnumerateArray().Select(v => v.GetProperty("id").GetString()));
        Assert.Equal(["id", "name", "count", "filters"], inbox.GetProperty("views")[0].EnumerateObject().Select(p => p.Name));
        Json("""{"id":"urgent","name":"Urgent","count":1,"filters":{"priority":"urgent","open":1}}""", inbox.GetProperty("views")[0]);
        Json("""{"assigned_to":"agent-1","status":"waiting_on_customer"}""", View(inbox, "waiting_on_customer").GetProperty("filters"));
        Assert.Equal(1, View(inbox, "waiting_on_customer").GetProperty("count").GetInt32());
        Json("""{"assigned_to":"agent-1","status":"waiting_on_agent"}""", View(inbox, "waiting_on_me").GetProperty("filters"));
        Json("""{"sla_at_risk":1}""", View(inbox, "sla_at_risk").GetProperty("filters"));
        Json("""{"open":1,"updated_within_hours":24,"sort_by":"updated_at"}""", View(inbox, "recently_updated").GetProperty("filters"));
        Assert.Equal(4, View(inbox, "recently_updated").GetProperty("count").GetInt32());

        Assert.Equal(["open", "urgent", "sla_at_risk", "resolved_today"], inbox.GetProperty("stats").EnumerateObject().Select(p => p.Name));
        // The "all" queue: four tickets with status open (a snoozed ticket keeps its
        // status here, and the status=open list the tile opens shows it), one urgent,
        // one resolved today.
        Json("""{"open":4,"urgent":1,"sla_at_risk":0,"resolved_today":1}""", inbox.GetProperty("stats"));
        Json("""{"key":"all","title":"All tickets"}""", props.GetProperty("queue"));
    }

    [Theory]
    [InlineData("?assigned_to=agent-1", "mine", "My inbox", 1, 1, 1)]
    [InlineData("?assigned=me", "mine", "My inbox", 1, 1, 1)]
    [InlineData("?assigned_to=me", "mine", "My inbox", 1, 1, 1)]
    [InlineData("?assigned_to=agent-1&priority=urgent", "mine", "My inbox", 1, 1, 1)]
    [InlineData("?unassigned=1", "unassigned", "Unassigned", 2, 1, 1)]
    [InlineData("", "all", "All tickets", 3, 2, 2)]
    [InlineData("?priority=urgent&open=1", "view:urgent", "Urgent", 2, 2, 0)]
    [InlineData("?view=urgent", "view:urgent", "Urgent", 2, 2, 0)]
    [InlineData("?view=nope", "all", "All tickets", 3, 2, 2)]
    public async Task TheQueueIsNamedAndScopedFromTheListQuery(string query, string key, string title, int open, int urgent, int resolvedToday)
    {
        var (host, client) = await StartAsync();
        await using var __ = host;
        var now = DateTime.UtcNow;
        await SeedAsync(host,
            T(t => { t.AssignedTo = Agent; t.Priority = TicketPriority.Urgent; }),
            T(Resolved(now, Agent)),
            T(),
            T(Resolved(now)),
            T(t => t.Priority = TicketPriority.Urgent));

        var props = await PropsAsync(client, Index + query);

        Json($$"""{"key":"{{key}}","title":"{{title}}"}""", props.GetProperty("queue"));
        Json($$"""{"open":{{open}},"urgent":{{urgent}},"sla_at_risk":0,"resolved_today":{{resolvedToday}}}""",
            props.GetProperty("inbox").GetProperty("stats"));
    }

    [Fact]
    public async Task OwnAndSharedSavedViewsAreListedWithCountsAndCanBeTheQueue()
    {
        var (host, client) = await StartAsync();
        await using var __ = host;
        SavedView mine = new() { Name = "High priority", UserId = Agent, Filters = """{"priority":"high"}""", Position = 1 };
        SavedView shared = new() { Name = "Waiting", UserId = Other, Filters = """{"status":"waiting_on_agent"}""", IsShared = true, Position = 2 };
        SavedView hidden = new() { Name = "Private", UserId = Other, Filters = """{"priority":"low"}""", Position = 0 };
        var high = T(t => t.Priority = TicketPriority.High);
        await host.SeedAsync(async db =>
        {
            db.SavedViews.AddRange(mine, shared, hidden);
            db.Tickets.AddRange(high, T(t => t.Priority = TicketPriority.High),
                T(t => { t.Priority = TicketPriority.High; t.Status = TicketStatus.Closed; }),
                T(t => t.Status = TicketStatus.WaitingOnAgent));
            await db.SaveChangesAsync();
        });

        var views = (await PropsAsync(client, Index)).GetProperty("inbox").GetProperty("views")
            .EnumerateArray().Where(v => v.GetProperty("id").ValueKind == JsonValueKind.Number).ToList();
        Assert.Equal(2, views.Count);
        Json("{\"id\":" + mine.Id + ""","name":"High priority","count":2,"filters":{"priority":"high"}}""", views[0]);
        Json("{\"id\":" + shared.Id + ""","name":"Waiting","count":1,"filters":{"status":"waiting_on_agent"}}""", views[1]);

        foreach (var query in new[] { $"?view={mine.Id}", "?priority=high" })
        {
            var props = await PropsAsync(client, Index + query);
            Json($$"""{"key":"view:{{mine.Id}}","title":"High priority"}""", props.GetProperty("queue"));
            Assert.Equal(3, props.GetProperty("tickets").GetProperty("total").GetInt32());
        }

        var notMine = await PropsAsync(client, Index + $"?view={hidden.Id}");
        Json("""{"key":"all","title":"All tickets"}""", notMine.GetProperty("queue"));
        Assert.Equal(4, notMine.GetProperty("tickets").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task EveryBuiltInViewCountsWhatItsOwnListShows()
    {
        var (host, client) = await StartAsync();
        await using var __ = host;
        var now = DateTime.UtcNow;
        await SeedAsync(host,
            T(t => { t.AssignedTo = Agent; t.Priority = TicketPriority.Urgent; }),
            T(t => { t.Priority = TicketPriority.Urgent; t.Status = TicketStatus.Closed; }),
            T(t => { t.AssignedTo = Agent; t.Status = TicketStatus.WaitingOnAgent; }),
            T(t => { t.AssignedTo = Agent; t.Status = TicketStatus.WaitingOnCustomer; }),
            T(t => t.SlaFirstResponseBreached = true),
            T(t => t.ResolutionDueAt = now.AddMinutes(30)),
            T(t => t.UpdatedAt = now.AddDays(-3)));

        var views = (await PropsAsync(client, Index)).GetProperty("inbox").GetProperty("views");
        foreach (var view in views.EnumerateArray())
        {
            var query = string.Join("&", view.GetProperty("filters").EnumerateObject()
                .Select(f => $"{f.Name}={Uri.EscapeDataString(f.Value.ToString())}"));
            var shown = (await PropsAsync(client, $"{Index}?{query}")).GetProperty("tickets").GetProperty("total").GetInt32();
            Assert.True(view.GetProperty("count").GetInt32() == shown, $"view {view.GetProperty("id")} counts {view.GetProperty("count")} but its list shows {shown}");
        }

        Assert.Equal([1, 2, 1, 1, 5], views.EnumerateArray().Select(v => v.GetProperty("count").GetInt32()));
    }

    [Fact]
    public async Task OnlyTicketsTheAgentMaySeeAreCounted()
    {
        var (host, client) = await StartAsync(options => options.Visibility = "department");
        await using var __ = host;
        await host.SeedAsync(async db =>
        {
            var support = new Department { Name = "Support", Slug = "support", IsActive = true };
            var billing = new Department { Name = "Billing", Slug = "billing", IsActive = true };
            db.Departments.AddRange(support, billing);
            await db.SaveChangesAsync();
            db.DepartmentAgents.Add(new DepartmentAgent { DepartmentId = support.Id, AgentId = Agent });
            db.Tickets.AddRange(
                T(t => { t.DepartmentId = support.Id; t.AssignedTo = Agent; t.Priority = TicketPriority.Urgent; }),
                T(t => t.DepartmentId = support.Id),
                T(t => t.DepartmentId = billing.Id),
                T(t => { t.DepartmentId = billing.Id; t.Priority = TicketPriority.Urgent; }),
                T(t => { t.DepartmentId = billing.Id; t.Status = TicketStatus.Resolved; t.ResolvedAt = DateTime.UtcNow; }));
            await db.SaveChangesAsync();
        });

        var inbox = (await PropsAsync(client, Index)).GetProperty("inbox");

        Json("""{"mine":1,"unassigned":1,"all":2}""", inbox.GetProperty("counts"));
        Json("""{"open":2,"urgent":1,"sla_at_risk":0,"resolved_today":0}""", inbox.GetProperty("stats"));
        Assert.Equal(1, View(inbox, "urgent").GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task TheListFiltersToTicketsWhoseSlaIsBreachedOrDueWithinTheHour()
    {
        var (host, client) = await StartAsync();
        await using var __ = host;
        var now = DateTime.UtcNow;
        var breached = T(t => t.SlaResolutionBreached = true);
        var responseDue = T(t => t.FirstResponseDueAt = now.AddMinutes(20));
        var resolutionDue = T(t => t.ResolutionDueAt = now.AddMinutes(50));
        await SeedAsync(host, breached, responseDue, resolutionDue,
            T(t => { t.FirstResponseDueAt = now.AddMinutes(20); t.FirstResponseAt = now.AddMinutes(-1); }),
            T(t => t.ResolutionDueAt = now.AddHours(3)),
            T(t => { t.SlaResolutionBreached = true; t.Status = TicketStatus.Resolved; t.ResolvedAt = now; }),
            T());

        var props = await PropsAsync(client, Index + "?sla_at_risk=1");

        Assert.Equal(new[] { breached.Id, responseDue.Id, resolutionDue.Id }.Order(), Ids(props).Order());
        Assert.Equal(3, props.GetProperty("inbox").GetProperty("stats").GetProperty("sla_at_risk").GetInt32());
    }

    [Fact]
    public async Task TheListFiltersToResolvedTodayOpenAndRecentlyUpdatedTickets()
    {
        var (host, client) = await StartAsync();
        await using var __ = host;
        var now = DateTime.UtcNow;
        var today = T(Resolved(now.Date.AddMinutes(1)));
        var fresh = T();
        await SeedAsync(host, today, fresh,
            T(Resolved(now.Date.AddDays(-1))),
            T(t => t.UpdatedAt = now.AddDays(-3)),
            T(t => { t.Status = TicketStatus.Closed; t.ClosedAt = now; }),
            T(t => t.SnoozedUntil = now.AddHours(1)));

        Assert.Equal([today.Id], Ids(await PropsAsync(client, Index + "?status=resolved&resolved_today=1")));
        Assert.Equal(2, (await PropsAsync(client, Index + "?open=1")).GetProperty("tickets").GetProperty("total").GetInt32());
        Assert.Equal([fresh.Id], Ids(await PropsAsync(client, Index + "?open=1&updated_within_hours=24")));
    }

    [Theory]
    [InlineData("sla_at_risk", "soon")]
    [InlineData("resolved_today", "maybe")]
    [InlineData("open", "perhaps")]
    [InlineData("updated_within_hours", "0")]
    [InlineData("updated_within_hours", "a%20day")]
    [InlineData("updated_within_hours", "8761")]
    public async Task MalformedValuesForTheNewFiltersGoBackWithErrors(string field, string value)
    {
        var (host, client) = await StartAsync();
        await using var __ = host;

        var response = await client.SendAsync(Visit(HttpMethod.Get, $"{Index}?{field}={value}"));

        Assert.True(response.StatusCode is HttpStatusCode.Found or HttpStatusCode.SeeOther, $"status {(int)response.StatusCode}");
        var cookie = FlashCookie(response);
        Assert.NotNull(cookie);
        var page = await PageAsync(client, Index, cookie);
        Assert.True(page.GetProperty("props").GetProperty("errors").TryGetProperty(field, out _),
            $"errors {page.GetProperty("props").GetProperty("errors")}");
    }

    [Fact]
    public async Task RowsPreviewTheLatestPublicReplyNeverAnInternalNote()
    {
        var (host, client) = await StartAsync();
        await using var __ = host;
        var ticket = T();
        await host.SeedAsync(async db =>
        {
            db.Tickets.Add(ticket);
            await db.SaveChangesAsync();
            db.Replies.AddRange(
                new Reply { TicketId = ticket.Id, Body = "Customer-visible answer", AuthorId = Agent, CreatedAt = DateTime.UtcNow.AddMinutes(-5) },
                new Reply { TicketId = ticket.Id, Body = "Secret internal note", AuthorId = Agent, IsInternalNote = true, Type = "note", CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        });

        var row = (await PropsAsync(client, Index)).GetProperty("tickets").GetProperty("data")[0];

        Assert.Equal("Customer-visible answer", row.GetProperty("latest_reply").GetProperty("body").GetString());
        Assert.False(row.GetProperty("latest_reply").GetProperty("is_internal_note").GetBoolean());
        // Escalated tracks no per-agent read state or customer presence.
        Assert.False(row.TryGetProperty("is_unread", out _));
        Assert.False(row.TryGetProperty("requester_online", out _));
    }

    [Fact]
    public async Task TheTicketPageGetsTheInboxAndTheQueueLastOpenedInTheList()
    {
        var (host, client) = await StartAsync();
        await using var __ = host;
        var ticket = T(t => t.AssignedTo = Agent);
        await SeedAsync(host, ticket, T());
        var show = $"{Index}/{ticket.Reference}";

        var page = await PageAsync(client, show);
        AssertPage(page, "Escalated/Agent/TicketShow");
        Json("""{"key":"all","title":"All tickets"}""", page.GetProperty("props").GetProperty("queue"));
        Json("""{"mine":1,"unassigned":1,"all":2}""", page.GetProperty("props").GetProperty("inbox").GetProperty("counts"));

        var list = await client.SendAsync(Visit(HttpMethod.Get, $"{Index}?assigned_to={Agent}"));
        var cookie = list.Headers.GetValues("Set-Cookie").Select(v => v.Split(';')[0])
            .Single(v => v.StartsWith(AgentInbox.QueueCookie + "=", StringComparison.Ordinal));

        var props = (await PageAsync(client, show, cookie)).GetProperty("props");
        Json("""{"key":"mine","title":"My inbox"}""", props.GetProperty("queue"));
        Json("""{"open":1,"urgent":0,"sla_at_risk":0,"resolved_today":0}""", props.GetProperty("inbox").GetProperty("stats"));

        // A cookie naming no queue selects nothing.
        props = (await PageAsync(client, show, AgentInbox.QueueCookie + "=view:bogus")).GetProperty("props");
        Json("""{"key":"all","title":"All tickets"}""", props.GetProperty("queue"));
    }

    [Fact]
    public async Task TheAgentTicketPagesSendEveryPropTheyDeclareAndNothingElse()
    {
        var (host, client) = await StartAsync();
        await using var __ = host;
        var ticket = T();
        await SeedAsync(host, ticket);

        // The props every page is shared: what the dashboard sends beyond its own.
        var dashboard = (await PropsAsync(client, "/support/agent")).EnumerateObject().Select(p => p.Name)
            .Except(["stats", "recentTickets", "needsAttention", "myPerformance"]).ToHashSet();

        var index = (await PropsAsync(client, Index)).EnumerateObject().Select(p => p.Name).Except(dashboard).Order();
        Assert.Equal(["departments", "filters", "inbox", "queue", "tags", "tickets"], index);

        // TicketShow also carries the ticket's custom field values, which this
        // package has sent there since custom fields shipped.
        var show = (await PropsAsync(client, $"{Index}/{ticket.Reference}")).EnumerateObject().Select(p => p.Name).Except(dashboard).Order();
        Assert.Equal(["agents", "cannedResponses", "customActions", "custom_fields", "departments", "followers_count", "inbox", "is_following", "macros", "queue", "tags", "ticket"],
            show.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task TheInboxIsKeptPerAgentForTheConfiguredSeconds()
    {
        var clock = new Clock();
        var (host, client) = await StartAsync(
            options => options.Ui.AgentInboxCacheSeconds = 30,
            services => services.AddSingleton<TimeProvider>(clock));
        await using var __ = host;
        var other = host.ClientFor(Other);

        await SeedAsync(host, T(t => t.AssignedTo = Agent));
        Assert.Equal(1, (await PropsAsync(client, Index)).GetProperty("inbox").GetProperty("counts").GetProperty("mine").GetInt32());

        await SeedAsync(host, T(t => t.AssignedTo = Agent), T(t => t.AssignedTo = Other));
        Assert.Equal(1, (await PropsAsync(client, Index)).GetProperty("inbox").GetProperty("counts").GetProperty("mine").GetInt32());
        Assert.Equal(1, (await PropsAsync(other, Index)).GetProperty("inbox").GetProperty("counts").GetProperty("mine").GetInt32());

        clock.Now = clock.Now.AddSeconds(31);
        Assert.Equal(2, (await PropsAsync(client, Index)).GetProperty("inbox").GetProperty("counts").GetProperty("mine").GetInt32());
    }
}
