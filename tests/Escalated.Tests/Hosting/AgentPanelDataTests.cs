using System.Net;
using System.Text;
using System.Text.Json;
using Escalated.Enums;
using Escalated.Events;
using Escalated.Hubs;
using Escalated.Models;
using Escalated.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Escalated.Tests.Hosting.InertiaTestSupport;

namespace Escalated.Tests.Hosting;

/// <summary>
/// The agent panel's data (escalated-developer-context ADR 2026-11-05-agent-panel-data,
/// after escalated-laravel #292-#296): agent status, knowledge-base search, unread markers,
/// customer presence and the agents' in-app notifications.
/// </summary>
public class AgentPanelDataTests
{
    private const string Agent = "agent-1";
    private const string Other = "agent-2";
    private const string Customer = "customer-1";
    private const string Index = "/support/agent/tickets";

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

    private static async Task<Ticket> CreateAsync(EscalatedTestHost host, int? departmentId = null)
    {
        using var scope = host.Services.CreateScope();
        var tickets = scope.ServiceProvider.GetRequiredService<TicketService>();
        return await tickets.CreateAsync("Printer on fire", "Help", Customer, "user", TicketPriority.Low, departmentId);
    }

    private static async Task ReplyAsync(EscalatedTestHost host, Ticket ticket, string? author, bool note = false, string? authorType = null)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Escalated.Data.EscalatedDbContext>();
        var fresh = await db.Tickets.FirstAsync(t => t.Id == ticket.Id);
        await scope.ServiceProvider.GetRequiredService<TicketService>().AddReplyAsync(fresh, "A reply", author, authorType, note);
    }

    private static async Task AssignAsync(EscalatedTestHost host, Ticket ticket, string agent, string? causer)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Escalated.Data.EscalatedDbContext>();
        var fresh = await db.Tickets.FirstAsync(t => t.Id == ticket.Id);
        await scope.ServiceProvider.GetRequiredService<AssignmentService>().AssignAsync(fresh, agent, causer);
    }

    /// <summary>Moves the ticket's activity and the agent's read into the past: both are kept to the second.</summary>
    private static Task BackdateAsync(EscalatedTestHost host, Ticket ticket) => host.SeedAsync(async db =>
    {
        await db.Tickets.IgnoreQueryFilters().Where(t => t.Id == ticket.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.LastCustomerActivityAt, DateTime.UtcNow.AddMinutes(-2)));
        await db.TicketReads.IgnoreQueryFilters().Where(r => r.TicketId == ticket.Id && r.LastReadAt != null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.LastReadAt, DateTime.UtcNow.AddMinutes(-1)));
    });

    private static async Task<JsonElement> RowAsync(HttpClient client, Ticket ticket)
    {
        var page = await PageAsync(client, Index);
        return page.GetProperty("props").GetProperty("tickets").GetProperty("data").EnumerateArray()
            .Single(r => r.GetProperty("id").GetInt32() == ticket.Id);
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> JsonAsync(HttpClient client, HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, string.IsNullOrEmpty(text) ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    // ---- Agent status ------------------------------------------------------------------

    [Fact]
    public async Task AgentsSetTheirStatusAndEveryStaffPageSharesIt()
    {
        var (host, client) = await StartAsync();
        await using var __ = host;

        var shared = (await PageAsync(client, Index)).GetProperty("props").GetProperty("escalated");
        Assert.Equal("offline", shared.GetProperty("agent_status").GetString());
        Assert.Equal(["online", "away", "offline"], shared.GetProperty("agent_statuses").EnumerateArray().Select(s => s.GetString()));
        Assert.Equal(0, shared.GetProperty("notifications").GetProperty("unread_count").GetInt32());

        var (status, body) = await JsonAsync(client, HttpMethod.Put, "/support/agent/status", new { status = "away" });
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("away", body.GetProperty("agent_status").GetString());
        Assert.Equal("away", await host.QueryAsync(db => db.AgentProfiles.Where(p => p.UserId == Agent).Select(p => p.ChatStatus).SingleAsync()));
        Assert.Equal("away", (await PageAsync(client, Index)).GetProperty("props").GetProperty("escalated").GetProperty("agent_status").GetString());

        Assert.Equal(HttpStatusCode.OK, (await JsonAsync(client, HttpMethod.Post, "/support/agent/status", new { status = "online" })).Status);

        var (invalid, errors) = await JsonAsync(client, HttpMethod.Put, "/support/agent/status", new { status = "busy" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid);
        Assert.True(errors.GetProperty("errors").TryGetProperty("status", out _));

        // An Inertia visit goes back.
        var visit = await client.SendAsync(Visit(HttpMethod.Put, "/support/agent/status", new { status = "away" }, referer: "http://localhost/support/agent/tickets"));
        Assert.True((int)visit.StatusCode is 302 or 303, $"Inertia PUT answered {(int)visit.StatusCode}");
    }

    [Fact]
    public async Task CustomersHaveNoStatusNorPanelProps()
    {
        var (host, _) = await StartAsync();
        await using var __ = host;
        var customer = host.ClientFor(Customer);

        var (status, _) = await JsonAsync(customer, HttpMethod.Put, "/support/agent/status", new { status = "away" });
        Assert.NotEqual(HttpStatusCode.OK, status);
        var shared = (await PageAsync(customer, "/support")).GetProperty("props").GetProperty("escalated");
        Assert.False(shared.TryGetProperty("agent_status", out _));
        Assert.False(shared.TryGetProperty("notifications", out _));
    }

    // ---- Knowledge base search ------------------------------------------------------------------

    [Fact]
    public async Task KnowledgeBaseSearchAnswersPublishedArticlesTitleMatchesFirst()
    {
        var (host, client) = await StartAsync();
        await using var __ = host;
        var now = DateTime.UtcNow;
        var ids = new Dictionary<string, int>();
        await host.SeedAsync(async db =>
        {
            var category = new ArticleCategory { Name = "Accounts", Slug = "accounts" };
            db.ArticleCategories.Add(category);
            await db.SaveChangesAsync();
            Article A(string title, string body, string status, int? categoryId, DateTime updated) => new()
            {
                Title = title,
                Slug = Guid.NewGuid().ToString("N"),
                Body = body,
                Status = status,
                CategoryId = categoryId,
                UpdatedAt = updated,
            };
            var articles = new Dictionary<string, Article>
            {
                ["body"] = A("Account basics", "<p>To reset your password open   Settings.</p>", "published", category.Id, now.AddSeconds(-10)),
                ["old"] = A("Password rules", "x", "published", null, now.AddSeconds(-500)),
                ["new"] = A("Password reset", "x", "published", null, now.AddSeconds(-100)),
                ["draft"] = A("Password draft", "x", "draft", null, now),
                ["percent"] = A("Save 50% today", "x", "published", null, now),
                ["dollars"] = A("Save 50 dollars today", "x", "published", null, now),
                ["snake"] = A("The snake_case guide", "x", "published", null, now),
                ["snakeX"] = A("The snakeXcase guide", "x", "published", null, now),
            };
            db.Articles.AddRange(articles.Values);
            await db.SaveChangesAsync();
            foreach (var (key, article) in articles) ids[key] = article.Id;
        });

        static List<int> Ids(JsonElement body) => body.GetProperty("data").EnumerateArray().Select(a => a.GetProperty("id").GetInt32()).ToList();

        var (status, body) = await JsonAsync(client, HttpMethod.Get, "/support/agent/kb/search?q=password");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal([ids["new"], ids["old"], ids["body"]], Ids(body));
        var item = body.GetProperty("data")[2];
        Assert.Equal(["id", "title", "excerpt", "url", "category", "updated_at"], item.EnumerateObject().Select(p => p.Name));
        Assert.Equal("To reset your password open Settings.", item.GetProperty("excerpt").GetString());
        Assert.StartsWith("http://localhost/support/kb/", item.GetProperty("url").GetString());
        Assert.Equal("Accounts", item.GetProperty("category").GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("data")[0].GetProperty("category").ValueKind);

        // Case-insensitive, with % and _ taken literally.
        Assert.Equal([ids["percent"]], Ids((await JsonAsync(client, HttpMethod.Get, "/support/agent/kb/search?q=" + Uri.EscapeDataString("50%"))).Body));
        Assert.Equal([ids["snake"]], Ids((await JsonAsync(client, HttpMethod.Get, "/support/agent/kb/search?q=SNAKE_CASE")).Body));

        foreach (var (query, field) in new[] { ("q=a", "q"), ("q=" + new string('a', 201), "q"), ("q=abc&limit=0", "limit"), ("q=abc&limit=21", "limit") })
        {
            var (bad, errors) = await JsonAsync(client, HttpMethod.Get, "/support/agent/kb/search?" + query);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, bad);
            Assert.True(errors.GetProperty("errors").TryGetProperty(field, out _), query);
        }

        Assert.Equal(2, Ids((await JsonAsync(client, HttpMethod.Get, "/support/agent/kb/search?q=password&limit=2")).Body).Count);
    }

    [Fact]
    public async Task KnowledgeBaseSearchIs404WhileTheKnowledgeBaseIsOff()
    {
        var (host, client) = await StartAsync(options => options.KnowledgeBase.Enabled = false);
        await using var __ = host;
        Assert.Equal(HttpStatusCode.NotFound, (await JsonAsync(client, HttpMethod.Get, "/support/agent/kb/search?q=password")).Status);
    }

    // ---- Unread markers ---------------------------------------------------------------------------

    [Fact]
    public async Task ANewTicketIsUnreadUntilTheAgentOpensItAndCustomerRepliesMakeItUnreadAgain()
    {
        var (host, client) = await StartAsync();
        await using var __ = host;
        var ticket = await CreateAsync(host);
        Assert.True((await RowAsync(client, ticket)).GetProperty("is_unread").GetBoolean());

        var show = await PageAsync(client, $"{Index}/{ticket.Reference}");
        Assert.False(show.GetProperty("props").GetProperty("ticket").GetProperty("is_unread").GetBoolean());
        Assert.False((await RowAsync(client, ticket)).GetProperty("is_unread").GetBoolean());
        // Per agent.
        Assert.True((await RowAsync(host.ClientFor(Other), ticket)).GetProperty("is_unread").GetBoolean());

        await BackdateAsync(host, ticket);
        await ReplyAsync(host, ticket, Other);
        await ReplyAsync(host, ticket, Other, note: true);
        await ReplyAsync(host, ticket, null, authorType: "system");
        Assert.False((await RowAsync(client, ticket)).GetProperty("is_unread").GetBoolean());

        await ReplyAsync(host, ticket, Customer);
        Assert.True((await RowAsync(client, ticket)).GetProperty("is_unread").GetBoolean());

        // A guest or inbound email (no author) is the customer side too.
        await PageAsync(client, $"{Index}/{ticket.Reference}");
        await BackdateAsync(host, ticket);
        await ReplyAsync(host, ticket, null);
        Assert.True((await RowAsync(client, ticket)).GetProperty("is_unread").GetBoolean());
    }

    [Fact]
    public async Task ImportedTicketsAreNotCustomerActivity()
    {
        var (host, client) = await StartAsync();
        await using var __ = host;
        var ticket = await ImportContext.SuppressAsync(() => CreateAsync(host));

        Assert.Null(await host.QueryAsync(db => db.Tickets.Where(t => t.Id == ticket.Id).Select(t => t.LastCustomerActivityAt).SingleAsync()));
        Assert.False((await RowAsync(client, ticket)).GetProperty("is_unread").GetBoolean());
    }

    [Fact]
    public async Task TheReadUnreadAndBulkRoutesSetTheMarkers()
    {
        var (host, client) = await StartAsync();
        await using var __ = host;
        var a = await CreateAsync(host);
        var b = await CreateAsync(host);

        var (status, body) = await JsonAsync(client, HttpMethod.Post, $"{Index}/{a.Reference}/read");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(body.GetProperty("is_unread").GetBoolean());
        Assert.False((await RowAsync(client, a)).GetProperty("is_unread").GetBoolean());
        (status, body) = await JsonAsync(client, HttpMethod.Post, $"{Index}/{a.Reference}/unread");
        Assert.True(body.GetProperty("is_unread").GetBoolean());
        Assert.True((await RowAsync(client, a)).GetProperty("is_unread").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await JsonAsync(client, HttpMethod.Post, $"{Index}/ESC-99999/read")).Status);

        await client.SendAsync(Visit(HttpMethod.Post, $"{Index}/bulk", new { ticket_ids = new[] { a.Id, b.Id }, action = "mark_read" }));
        Assert.False((await RowAsync(client, a)).GetProperty("is_unread").GetBoolean());
        Assert.False((await RowAsync(client, b)).GetProperty("is_unread").GetBoolean());
        await client.SendAsync(Visit(HttpMethod.Post, $"{Index}/bulk", new { ticket_ids = new[] { a.Id }, action = "mark_unread" }));
        Assert.True((await RowAsync(client, a)).GetProperty("is_unread").GetBoolean());
        Assert.False((await RowAsync(client, b)).GetProperty("is_unread").GetBoolean());
    }

    [Fact]
    public async Task InboxUnreadCountsUnreadOpenTicketsPerQueue()
    {
        var (host, client) = await StartAsync();
        await using var __ = host;
        var mine = await CreateAsync(host);
        var minePending = await CreateAsync(host);
        await AssignAsync(host, mine, Agent, Other);
        await AssignAsync(host, minePending, Agent, Other);
        var unassigned = await CreateAsync(host);
        var resolved = await CreateAsync(host);
        await host.SeedAsync(db => db.Tickets.Where(t => t.Id == resolved.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, TicketStatus.Resolved).SetProperty(t => t.ResolvedAt, DateTime.UtcNow)));
        await PageAsync(client, $"{Index}/{mine.Reference}");
        await PageAsync(client, $"{Index}/{unassigned.Reference}");

        var inbox = (await PageAsync(client, Index)).GetProperty("props").GetProperty("inbox");
        Assert.Equal(["counts", "unread", "views", "stats"], inbox.EnumerateObject().Select(p => p.Name));
        var unread = inbox.GetProperty("unread");
        Assert.Equal(1, unread.GetProperty("mine").GetInt32());
        Assert.Equal(0, unread.GetProperty("unassigned").GetInt32());
        Assert.Equal(1, unread.GetProperty("all").GetInt32());
    }

    [Fact]
    public async Task UnderDepartmentVisibilityOnlyVisibleTicketsAreCountedAndMarkedAndOthersAreRedacted()
    {
        var (host, client) = await StartAsync(options => options.Visibility = "department");
        await using var __ = host;
        int own = 0, hiddenDepartment = 0;
        await host.SeedAsync(async db =>
        {
            var a = new Department { Name = "Own", Slug = "own" };
            var b = new Department { Name = "Hidden", Slug = "hidden" };
            db.Departments.AddRange(a, b);
            await db.SaveChangesAsync();
            db.DepartmentAgents.Add(new DepartmentAgent { DepartmentId = a.Id, AgentId = Agent });
            await db.SaveChangesAsync();
            own = a.Id;
            hiddenDepartment = b.Id;
        });
        var visible = await CreateAsync(host, own);
        var hidden = await CreateAsync(host, hiddenDepartment);

        var rows = (await PageAsync(client, Index)).GetProperty("props").GetProperty("tickets").GetProperty("data");
        Assert.Equal([visible.Id], rows.EnumerateArray().Select(r => r.GetProperty("id").GetInt32()));
        Assert.Equal(1, (await PageAsync(client, Index)).GetProperty("props").GetProperty("inbox").GetProperty("unread").GetProperty("all").GetInt32());

        Assert.Equal(HttpStatusCode.NotFound, (await JsonAsync(client, HttpMethod.Post, $"{Index}/{hidden.Reference}/read")).Status);
        Assert.Equal(0, await host.QueryAsync(db => db.TicketReads.IgnoreQueryFilters().CountAsync(r => r.TicketId == hidden.Id)));

        using (var scope = host.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AgentPanelService>().NotifyAsync(Agent, "ticket_assigned", hidden, null, null);
        }

        var (_, feed) = await JsonAsync(client, HttpMethod.Get, "/support/agent/notifications");
        var item = feed.GetProperty("data")[0];
        Assert.Equal("A ticket you can no longer view", item.GetProperty("title").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("body").ValueKind);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("ticket").ValueKind);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("url").ValueKind);
        Assert.DoesNotContain(hidden.Reference, item.GetRawText());
    }

    // ---- Customer presence ---------------------------------------------------------------------------

    [Fact]
    public async Task ACustomerOnThePortalShowsOnlineOnAgentPagesOnly()
    {
        var (host, client) = await StartAsync();
        await using var __ = host;
        var ticket = await CreateAsync(host);
        var row = await RowAsync(client, ticket);
        Assert.False(row.GetProperty("requester_online").GetBoolean());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("requester_last_seen_at").ValueKind);

        var customer = host.ClientFor(Customer);
        var customerPage = await PageAsync(customer, $"/support/{ticket.Reference}");
        Assert.DoesNotContain("requester_online", customerPage.GetRawText());
        Assert.DoesNotContain("requester_last_seen_at", customerPage.GetRawText());
        Assert.DoesNotContain("is_unread", customerPage.GetRawText());

        row = await RowAsync(client, ticket);
        Assert.True(row.GetProperty("requester_online").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, row.GetProperty("requester_last_seen_at").ValueKind);

        // Throttled: a visit within 30 seconds writes nothing.
        var recent = DateTime.UtcNow.AddSeconds(-10);
        await host.SeedAsync(db => db.CustomerPresence.ExecuteUpdateAsync(s => s.SetProperty(p => p.LastSeenAt, recent)));
        await PageAsync(customer, "/support");
        Assert.True(await host.QueryAsync(db => db.CustomerPresence.SingleAsync()) is { } kept && kept.LastSeenAt < DateTime.UtcNow.AddSeconds(-5));
        await host.SeedAsync(db => db.CustomerPresence.ExecuteUpdateAsync(s => s.SetProperty(p => p.LastSeenAt, DateTime.UtcNow.AddSeconds(-40))));
        await PageAsync(customer, "/support");
        Assert.True((await host.QueryAsync(db => db.CustomerPresence.SingleAsync())).LastSeenAt > DateTime.UtcNow.AddSeconds(-5));

        // Seen outside the window: offline, with the time.
        await host.SeedAsync(db => db.CustomerPresence.ExecuteUpdateAsync(s => s.SetProperty(p => p.LastSeenAt, DateTime.UtcNow.AddMinutes(-10))));
        row = await RowAsync(client, ticket);
        Assert.False(row.GetProperty("requester_online").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, row.GetProperty("requester_last_seen_at").ValueKind);
    }

    [Fact]
    public async Task StaffBrowsingThePortalAreNotRecorded()
    {
        var (host, client) = await StartAsync();
        await using var __ = host;
        await client.SendAsync(Visit(HttpMethod.Get, "/support"));
        Assert.Equal(0, await host.QueryAsync(db => db.CustomerPresence.CountAsync()));
    }

    // ---- Notifications -----------------------------------------------------------------------------------

    [Fact]
    public async Task AssignmentBySomeoneElseNotifiesAssigningYourselfDoesNot()
    {
        var hub = new RecordingHubContext();
        var (host, client) = await StartAsync(services: s => s.AddSingleton<IHubContext<EscalatedHub>>(hub));
        await using var __ = host;
        var ticket = await CreateAsync(host);

        await AssignAsync(host, ticket, Agent, Agent);
        Assert.Equal(0, await host.QueryAsync(db => db.AgentNotifications.CountAsync(n => n.UserId == Agent)));

        await AssignAsync(host, ticket, Agent, Other);
        var (_, body) = await JsonAsync(client, HttpMethod.Get, "/support/agent/notifications");
        Assert.Equal("""{"current_page":1,"last_page":1,"per_page":20,"total":1}""", body.GetProperty("meta").GetRawText());
        var item = body.GetProperty("data")[0];
        Assert.Equal(["id", "type", "title", "body", "ticket", "url", "created_at", "read_at"], item.EnumerateObject().Select(p => p.Name));
        Assert.Equal("ticket_assigned", item.GetProperty("type").GetString());
        Assert.Equal($"{ticket.Reference} was assigned to you", item.GetProperty("title").GetString());
        Assert.Equal(ticket.Subject, item.GetProperty("body").GetString());
        Assert.Equal(ticket.Reference, item.GetProperty("ticket").GetProperty("reference").GetString());
        Assert.Equal($"/support/agent/tickets/{ticket.Reference}", item.GetProperty("url").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("read_at").ValueKind);
        Assert.Equal(1, (await PageAsync(client, Index)).GetProperty("props").GetProperty("escalated").GetProperty("notifications").GetProperty("unread_count").GetInt32());

        // Sent to the agent's own group only.
        var sent = Assert.Single(hub.Sent);
        Assert.Equal((AgentPanelService.NotificationGroup(Agent), "notification.created"), (sent.Group, sent.Method));
    }

    [Fact]
    public async Task OnlyCustomerSideRepliesNotifyTheAssigneeAndFollowers()
    {
        var (host, _) = await StartAsync();
        await using var __ = host;
        var ticket = await CreateAsync(host);
        await AssignAsync(host, ticket, Agent, Other);
        await host.SeedAsync(async db =>
        {
            db.TicketFollowers.Add(new TicketFollower { TicketId = ticket.Id, UserId = Other });
            await db.SaveChangesAsync();
        });

        await ReplyAsync(host, ticket, Other);
        await ReplyAsync(host, ticket, Other, note: true);
        Assert.Equal(0, await host.QueryAsync(db => db.AgentNotifications.CountAsync(n => n.Type == "ticket_reply")));

        await ReplyAsync(host, ticket, Customer);
        Assert.Equal(1, await host.QueryAsync(db => db.AgentNotifications.CountAsync(n => n.Type == "ticket_reply" && n.UserId == Agent)));
        Assert.Equal(1, await host.QueryAsync(db => db.AgentNotifications.CountAsync(n => n.Type == "ticket_reply" && n.UserId == Other)));
    }

    [Fact]
    public async Task SlaEventsAndChatTransfersNotify()
    {
        var (host, client) = await StartAsync();
        await using var __ = host;
        var ticket = await CreateAsync(host);
        await AssignAsync(host, ticket, Agent, Other);

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Escalated.Data.EscalatedDbContext>();
            var assigned = await db.Tickets.FirstAsync(t => t.Id == ticket.Id);
            var events = scope.ServiceProvider.GetRequiredService<EscalatedEventDispatcher>();
            await events.DispatchAsync(new SlaWarningEvent(assigned, "resolution", 20));
            await events.DispatchAsync(new SlaBreachedEvent(assigned, "resolution"));
            await events.DispatchAsync(new ChatSessionTransferredEvent(new ChatSession { Id = 7, TicketId = ticket.Id }, Other, Agent, null));
        }

        var (_, body) = await JsonAsync(client, HttpMethod.Get, "/support/agent/notifications");
        var types = body.GetProperty("data").EnumerateArray().Select(i => i.GetProperty("type").GetString()).ToList();
        Assert.Contains("sla_warning", types);
        Assert.Contains("sla_breached", types);
        Assert.Contains("chat_transferred", types);
        Assert.Contains(body.GetProperty("data").EnumerateArray(), i => i.GetProperty("title").GetString() == $"SLA breached on {ticket.Reference}");
    }

    [Fact]
    public async Task ReadReadAllTheUnreadFilterAndPaging()
    {
        var (host, client) = await StartAsync();
        await using var __ = host;
        var ticket = await CreateAsync(host);
        using (var scope = host.Services.CreateScope())
        {
            var panel = scope.ServiceProvider.GetRequiredService<AgentPanelService>();
            for (var i = 0; i < 3; i++) await panel.NotifyAsync(Agent, "ticket_assigned", ticket, null, null);
            await panel.NotifyAsync(Other, "ticket_assigned", ticket, null, null);
        }

        var first = (await JsonAsync(client, HttpMethod.Get, "/support/agent/notifications")).Body.GetProperty("data")[0].GetProperty("id").GetString();
        var others = (await JsonAsync(host.ClientFor(Other), HttpMethod.Get, "/support/agent/notifications")).Body.GetProperty("data")[0].GetProperty("id").GetString();

        var (status, read) = await JsonAsync(client, HttpMethod.Post, $"/support/agent/notifications/{first}/read");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(2, read.GetProperty("unread_count").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await JsonAsync(client, HttpMethod.Post, $"/support/agent/notifications/{others}/read")).Status);
        Assert.Equal(2, (await JsonAsync(client, HttpMethod.Get, "/support/agent/notifications?unread=1")).Body.GetProperty("meta").GetProperty("total").GetInt32());

        var paged = (await JsonAsync(client, HttpMethod.Get, "/support/agent/notifications?per_page=2&page=2")).Body;
        Assert.Equal("""{"current_page":2,"last_page":2,"per_page":2,"total":3}""", paged.GetProperty("meta").GetRawText());
        Assert.Equal(1, paged.GetProperty("data").GetArrayLength());
        Assert.Equal(50, (await JsonAsync(client, HttpMethod.Get, "/support/agent/notifications?per_page=500")).Body.GetProperty("meta").GetProperty("per_page").GetInt32());

        var (_, all) = await JsonAsync(client, HttpMethod.Post, "/support/agent/notifications/read-all");
        Assert.Equal(0, all.GetProperty("unread_count").GetInt32());
        Assert.Equal(0, await host.QueryAsync(db => db.AgentNotifications.CountAsync(n => n.UserId == Agent && n.ReadAt == null)));
        Assert.Equal(1, await host.QueryAsync(db => db.AgentNotifications.CountAsync(n => n.UserId == Other && n.ReadAt == null)));
    }

    [Fact]
    public async Task SwitchedOffNothingIsStoredTheRoutesAre404AndTheCountIsNotShared()
    {
        var (host, client) = await StartAsync(options => options.Ui.AgentNotifications = false);
        await using var __ = host;
        var ticket = await CreateAsync(host);
        await AssignAsync(host, ticket, Agent, Other);

        Assert.Equal(0, await host.QueryAsync(db => db.AgentNotifications.CountAsync()));
        Assert.Equal(HttpStatusCode.NotFound, (await JsonAsync(client, HttpMethod.Get, "/support/agent/notifications")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await JsonAsync(client, HttpMethod.Post, "/support/agent/notifications/read-all")).Status);
        Assert.False((await PageAsync(client, Index)).GetProperty("props").GetProperty("escalated").TryGetProperty("notifications", out _));
    }

    /// <summary>An <see cref="IHubContext{THub}"/> that records what is sent to groups.</summary>
    private sealed class RecordingHubContext : IHubContext<EscalatedHub>
    {
        public List<(string Group, string Method, object?[] Args)> Sent { get; } = new();

        public IHubClients Clients => new RecordingClients(this);

        public IGroupManager Groups => throw new NotSupportedException();
    }

    private sealed class RecordingClients(RecordingHubContext owner) : IHubClients
    {
        public IClientProxy All => new Proxy(owner, "*");

        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => new Proxy(owner, "*");

        public IClientProxy Client(string connectionId) => new Proxy(owner, "client:" + connectionId);

        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => new Proxy(owner, "clients");

        public IClientProxy Group(string groupName) => new Proxy(owner, groupName);

        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => new Proxy(owner, groupName);

        public IClientProxy Groups(IReadOnlyList<string> groupNames) => new Proxy(owner, "groups");

        public IClientProxy User(string userId) => new Proxy(owner, "user:" + userId);

        public IClientProxy Users(IReadOnlyList<string> userIds) => new Proxy(owner, "users");
    }

    private sealed class Proxy(RecordingHubContext owner, string target) : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            owner.Sent.Add((target, method, args));
            return Task.CompletedTask;
        }
    }
}
