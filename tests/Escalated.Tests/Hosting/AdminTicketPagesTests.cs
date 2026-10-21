using System.Net;
using System.Net.Http.Json;
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
/// The admin ticket pages (<c>Admin/Tickets/Index</c>, <c>Admin/Tickets/Show</c>)
/// and their form posts, against escalated-laravel's <c>Admin/TicketController</c>,
/// plus the pin, presence and saved-view calls the pages' components make.
/// </summary>
public class AdminTicketPagesTests
{
    private const string Admin = "admin-1";
    private const string Agent = "agent-1";
    private const string Customer = "customer-1";

    private sealed class Directory : IUserDirectory
    {
        private static readonly Dictionary<string, UserDirectoryEntry> Users = new()
        {
            [Admin] = new(Admin, "Ada Admin", "ada@example.com"),
            [Agent] = new(Agent, "Avery Agent", "avery@example.com"),
            [Customer] = new(Customer, "Casey Customer", "casey@example.com"),
        };

        public Task<UserDirectoryPage> ListAsync(string? search, int page, int pageSize, CancellationToken ct = default) =>
            Task.FromResult(new UserDirectoryPage(Users.Values.ToList(), Users.Count, page, pageSize));

        public Task<UserDirectoryEntry?> FindAsync(string id, CancellationToken ct = default) =>
            Task.FromResult(Users.GetValueOrDefault(id));
    }

    private sealed record Seeded(int TicketId, int NoteId, int ReplyId, int OtherDepartmentId, int TagId);

    private static async Task<(EscalatedTestHost Host, HttpClient Client, Seeded Seed)> StartAsync()
    {
        var host = await EscalatedTestHost.StartAsync(
            options => options.Ui.Enabled = true,
            services => services.AddSingleton<IUserDirectory, Directory>());
        await host.GrantRoleAsync(Admin, "escalated-admin");
        await host.GrantRoleAsync(Agent, "escalated-agent");

        Seeded? seeded = null;
        await host.SeedAsync(async db =>
        {
            var billing = new Department { Name = "Billing", Slug = "billing", IsActive = true };
            var support = new Department { Name = "Support", Slug = "support", IsActive = true };
            var vip = new Tag { Name = "VIP", Slug = "vip", Color = "#ff0000" };
            db.AddRange(billing, support, vip);
            await db.SaveChangesAsync();

            var ticket = new Ticket
            {
                Reference = "ESC-00001",
                Subject = "Card declined",
                Description = "Declined twice.",
                Priority = TicketPriority.Urgent,
                RequesterType = "User",
                RequesterId = Customer,
                DepartmentId = billing.Id,
                CreatedAt = DateTime.UtcNow.AddHours(-2),
            };
            db.Tickets.AddRange(ticket, new Ticket { Reference = "ESC-00002", Subject = "Invoice copy", AssignedTo = Agent });
            await db.SaveChangesAsync();

            db.TicketTags.Add(new TicketTag { TicketId = ticket.Id, TagId = vip.Id });
            var reply = new Reply { TicketId = ticket.Id, Body = "Please retry.", AuthorId = Agent, CreatedAt = DateTime.UtcNow.AddMinutes(-30) };
            var note = new Reply { TicketId = ticket.Id, Body = "Gateway is flaky.", AuthorId = Agent, IsInternalNote = true, Type = "note", CreatedAt = DateTime.UtcNow.AddMinutes(-20) };
            db.Replies.AddRange(reply, note);
            db.CannedResponses.Add(new CannedResponse { Title = "Thanks", Body = "Thanks for writing.", IsShared = true });
            db.Macros.Add(new Macro { Name = "Escalate", Actions = """[{"type":"priority","value":"high"}]""", IsShared = true });
            db.SavedViews.AddRange(
                new SavedView { Name = "Urgent", UserId = Admin, Filters = """{"priority":"urgent"}""", Position = 1 },
                new SavedView { Name = "Team", UserId = Agent, Filters = """{"status":"open"}""", IsShared = true, Position = 2 },
                new SavedView { Name = "Private", UserId = Agent, Filters = "{}", Position = 0 });
            await db.SaveChangesAsync();

            seeded = new Seeded(ticket.Id, note.Id, reply.Id, support.Id, vip.Id);
        });

        return (host, host.ClientFor(Admin), seeded!);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string action, object body)
    {
        var response = await client.SendAsync(Visit(HttpMethod.Post, $"/support/admin/tickets/ESC-00001/{action}", body));
        Assert.True(response.StatusCode == HttpStatusCode.SeeOther, $"{action}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        Assert.Equal("/support/admin/tickets/ESC-00001", response.Headers.Location!.ToString());
        return response;
    }

    private static Task<Ticket> TicketAsync(EscalatedTestHost host) =>
        host.QueryAsync(db => db.Tickets.AsNoTracking().SingleAsync(t => t.Reference == "ESC-00001"));

    [Fact]
    public async Task Index_RendersTheQueueWithAgentsAndSavedViews()
    {
        var (host, client, seed) = await StartAsync();
        await using var __ = host;

        var page = await PageAsync(client, "/support/admin/tickets?priority=urgent");
        AssertPage(page, "Escalated/Admin/Tickets/Index");
        var props = page.GetProperty("props");

        var tickets = Keys(props.GetProperty("tickets"), "data", "current_page", "last_page", "per_page", "total", "links");
        Assert.Equal(1, tickets.GetProperty("total").GetInt32());
        var row = Keys(tickets.GetProperty("data")[0], "id", "reference", "subject", "status", "priority", "requester",
            "assignee", "department", "tags", "latest_reply", "created_at", "updated_at");
        Assert.Equal("ESC-00001", row.GetProperty("reference").GetString());
        Assert.Equal("Casey Customer", row.GetProperty("requester").GetProperty("name").GetString());
        Assert.Equal("urgent", props.GetProperty("filters").GetProperty("priority").GetString());
        Keys(props.GetProperty("departments")[0], "id", "name");
        Keys(props.GetProperty("tags")[0], "id", "name", "color");

        var agents = props.GetProperty("agents").EnumerateArray().ToList();
        Assert.Equal(new[] { "Ada Admin", "Avery Agent" }, agents.Select(a => a.GetProperty("name").GetString()));
        Keys(agents[0], "id", "name", "email");

        var views = props.GetProperty("savedViews").EnumerateArray().ToList();
        Assert.Equal(new[] { "Urgent", "Team" }, views.Select(v => v.GetProperty("name").GetString()));
        var view = Keys(views[0], "id", "name", "filters", "is_shared", "position", "user_id");
        Assert.Equal("urgent", view.GetProperty("filters").GetProperty("priority").GetString());

        var all = await PageAsync(client, "/support/admin/tickets");
        Assert.Equal(2, all.GetProperty("props").GetProperty("tickets").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Index_StillAnswersTheJsonApiForApiClients()
    {
        var (host, client, _) = await StartAsync();
        await using var __ = host;

        var response = await client.GetAsync("/support/admin/tickets");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("X-Inertia"));
        Assert.StartsWith("application/json", response.Content.Headers.ContentType!.ToString());
    }

    [Fact]
    public async Task Show_RendersTheTicketAgentsAndSnoozeState()
    {
        var (host, client, _) = await StartAsync();
        await using var __ = host;

        var page = await PageAsync(client, "/support/admin/tickets/ESC-00001");
        AssertPage(page, "Escalated/Admin/Tickets/Show");
        var props = page.GetProperty("props");

        var ticket = Keys(props.GetProperty("ticket"), "id", "reference", "subject", "description", "status", "priority",
            "requester", "assignee", "department", "tags", "replies", "pinned_notes", "attachments", "activities",
            "satisfaction_rating", "related_tickets", "requester_ticket_count", "subjects", "snoozed_until", "is_snoozed",
            "sla_policy", "chat_messages");
        Assert.Equal("Billing", ticket.GetProperty("department").GetProperty("name").GetString());
        var reply = Keys(ticket.GetProperty("replies")[0], "id", "body", "is_internal_note", "is_pinned", "author", "attachments", "created_at");
        Assert.Equal("Avery Agent", reply.GetProperty("author").GetProperty("name").GetString());

        Assert.Equal(2, props.GetProperty("agents").GetArrayLength());
        Assert.False(props.GetProperty("is_snoozed").GetBoolean());
        Assert.Equal("Thanks", props.GetProperty("cannedResponses")[0].GetProperty("title").GetString());
        Assert.Equal("Escalate", props.GetProperty("macros")[0].GetProperty("name").GetString());
        Assert.False(props.GetProperty("is_following").GetBoolean());
        Assert.Equal(0, props.GetProperty("followers_count").GetInt32());
    }

    [Fact]
    public async Task Show_IsA404ForAnUnknownReference()
    {
        var (host, client, _) = await StartAsync();
        await using var __ = host;

        var response = await client.SendAsync(Visit(HttpMethod.Get, "/support/admin/tickets/ESC-99999"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task TheAdminPages_AreForAdminsOnly()
    {
        var (host, _, _) = await StartAsync();
        await using var __ = host;
        var agent = host.ClientFor(Agent);

        var index = await agent.SendAsync(Visit(HttpMethod.Get, "/support/admin/tickets"));
        var show = await agent.SendAsync(Visit(HttpMethod.Get, "/support/admin/tickets/ESC-00001"));
        var reply = await agent.SendAsync(Visit(HttpMethod.Post, "/support/admin/tickets/ESC-00001/reply", new { body = "x" }));

        Assert.Equal(HttpStatusCode.Forbidden, index.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, show.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, reply.StatusCode);
    }

    [Fact]
    public async Task FormPosts_ChangeTheTicketAndComeBackToTheAdminPage()
    {
        var (host, client, seed) = await StartAsync();
        await using var __ = host;

        await PostAsync(client, "reply", new { body = "We refunded you." });
        await PostAsync(client, "note", new { body = "Refund id 42" });
        await PostAsync(client, "status", new { status = "in_progress" });
        await PostAsync(client, "priority", new { priority = "low" });
        await PostAsync(client, "assign", new { agent_id = Agent });
        await PostAsync(client, "department", new { department_id = seed.OtherDepartmentId });
        await PostAsync(client, "tags", new { tag_ids = Array.Empty<int>() });
        await PostAsync(client, "follow", new { });

        var ticket = await TicketAsync(host);
        Assert.Equal(TicketStatus.InProgress, ticket.Status);
        Assert.Equal(TicketPriority.Low, ticket.Priority);
        Assert.Equal(Agent, ticket.AssignedTo);
        Assert.Equal(seed.OtherDepartmentId, ticket.DepartmentId);
        Assert.False(await host.QueryAsync(db => db.TicketTags.AnyAsync(tt => tt.TicketId == seed.TicketId)));
        Assert.True(await host.QueryAsync(db => db.TicketFollowers.AnyAsync(f => f.TicketId == seed.TicketId && f.UserId == Admin)));
        var bodies = await host.QueryAsync(db => db.Replies.Where(r => r.TicketId == seed.TicketId && r.AuthorId == Admin)
            .OrderBy(r => r.Id).Select(r => new { r.Body, r.IsInternalNote }).ToListAsync());
        Assert.Equal(new[] { ("We refunded you.", false), ("Refund id 42", true) }, bodies.Select(b => (b.Body, b.IsInternalNote)));

        var macroId = await host.QueryAsync(db => db.Macros.Select(m => m.Id).SingleAsync());
        await PostAsync(client, "macro", new { macro_id = macroId });
        Assert.Equal(TicketPriority.High, (await TicketAsync(host)).Priority);
    }

    [Fact]
    public async Task Bulk_ChangesTheChosenTicketsAndComesBackToTheAdminQueue()
    {
        var (host, client, _) = await StartAsync();
        await using var __ = host;
        var ids = await host.QueryAsync(db => db.Tickets.Select(t => t.Id).ToListAsync());

        var response = await client.SendAsync(Visit(HttpMethod.Post, "/support/admin/tickets/bulk",
            new { ticket_ids = ids, action = "priority", value = "critical" }));

        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        Assert.Equal("/support/admin/tickets", response.Headers.Location!.ToString());
        var priorities = await host.QueryAsync(db => db.Tickets.Select(t => t.Priority).ToListAsync());
        Assert.All(priorities, p => Assert.Equal(TicketPriority.Critical, p));
    }

    [Fact]
    public async Task SnoozeAndUnsnooze_SetAndClearTheSnooze()
    {
        var (host, client, _) = await StartAsync();
        await using var __ = host;
        var until = DateTime.UtcNow.AddDays(1);

        await PostAsync(client, "snooze", new { snoozed_until = until.ToString("O") });

        var snoozed = await TicketAsync(host);
        Assert.NotNull(snoozed.SnoozedUntil);
        Assert.True(Math.Abs((snoozed.SnoozedUntil!.Value - until).TotalSeconds) < 1);
        var page = await PageAsync(client, "/support/admin/tickets/ESC-00001");
        Assert.True(page.GetProperty("props").GetProperty("is_snoozed").GetBoolean());

        await PostAsync(client, "unsnooze", new { });
        Assert.Null((await TicketAsync(host)).SnoozedUntil);
    }

    [Theory]
    [InlineData("not a date")]
    [InlineData("2001-01-01T00:00:00Z")]
    public async Task Snooze_RejectsABadOrPastTime(string value)
    {
        var (host, client, _) = await StartAsync();
        await using var __ = host;

        var response = await PostAsync(client, "snooze", new { snoozed_until = value });

        var page = await PageAsync(client, "/support/admin/tickets/ESC-00001", FlashCookie(response));
        Assert.True(page.GetProperty("props").GetProperty("errors").TryGetProperty("snoozed_until", out _));
        Assert.Null((await TicketAsync(host)).SnoozedUntil);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("agent")]
    public async Task Pin_TogglesAnInternalNoteAndRefusesAPublicReply(string area)
    {
        var (host, _, seed) = await StartAsync();
        await using var __ = host;
        var client = host.ClientFor(area == "admin" ? Admin : Agent);
        var url = $"/support/{area}/tickets/ESC-00001/replies";

        var pin = await client.SendAsync(Visit(HttpMethod.Post, $"{url}/{seed.NoteId}/pin", new { }));
        Assert.Equal(HttpStatusCode.SeeOther, pin.StatusCode);
        Assert.True(await host.QueryAsync(db => db.Replies.Where(r => r.Id == seed.NoteId).Select(r => r.IsPinned).SingleAsync()));

        await client.SendAsync(Visit(HttpMethod.Post, $"{url}/{seed.NoteId}/pin", new { }));
        Assert.False(await host.QueryAsync(db => db.Replies.Where(r => r.Id == seed.NoteId).Select(r => r.IsPinned).SingleAsync()));

        var refused = await client.SendAsync(Visit(HttpMethod.Post, $"{url}/{seed.ReplyId}/pin", new { }));
        Assert.Equal(HttpStatusCode.SeeOther, refused.StatusCode);
        Assert.False(await host.QueryAsync(db => db.Replies.Where(r => r.Id == seed.ReplyId).Select(r => r.IsPinned).SingleAsync()));
        var page = await PageAsync(client, $"/support/{area}/tickets/ESC-00001", FlashCookie(refused));
        Assert.Equal("Only internal notes can be pinned.", page.GetProperty("props").GetProperty("flash").GetProperty("error").GetString());
    }

    [Fact]
    public async Task Presence_ListsTheOtherStaffViewingTheTicket()
    {
        var (host, admin, _) = await StartAsync();
        await using var __ = host;
        var agent = host.ClientFor(Agent);

        var first = await PresenceAsync(admin, "admin");
        Assert.Equal(0, first.GetProperty("viewers").GetArrayLength());

        var second = await PresenceAsync(agent, "agent");
        var viewer = Assert.Single(second.GetProperty("viewers").EnumerateArray());
        Assert.Equal(Admin, viewer.GetProperty("id").GetString());
        Assert.Equal("Ada Admin", viewer.GetProperty("name").GetString());

        static async Task<JsonElement> PresenceAsync(HttpClient client, string area)
        {
            var response = await client.PostAsync($"/support/{area}/tickets/ESC-00001/presence", JsonContent.Create(new { }));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
        }
    }

    [Fact]
    public async Task SavedViews_AcceptWhatTheViewDialogsSend()
    {
        var (host, client, _) = await StartAsync();
        await using var __ = host;

        var created = await client.PostAsync("/support/admin/saved-views",
            JsonContent.Create(new { name = "Mine", filters = new { assigned_to = Admin, status = "open" }, is_shared = true }));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var view = await host.QueryAsync(db => db.SavedViews.AsNoTracking().SingleAsync(v => v.Name == "Mine"));
        Assert.True(view.IsShared);
        Assert.Equal("open", JsonDocument.Parse(view.Filters).RootElement.GetProperty("status").GetString());

        var renamed = await client.PutAsync($"/support/admin/saved-views/{view.Id}",
            JsonContent.Create(new { name = "Mine (open)", filters = new { status = "open" } }));
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        view = await host.QueryAsync(db => db.SavedViews.AsNoTracking().SingleAsync(v => v.Id == view.Id));
        Assert.Equal("Mine (open)", view.Name);
        Assert.Equal("""{"status":"open"}""", view.Filters);

        // API clients sending filters as a string keep working.
        var legacy = await client.PostAsync("/support/admin/saved-views",
            JsonContent.Create(new { name = "Legacy", filters = """{"priority":"high"}""", isShared = true }));
        Assert.Equal(HttpStatusCode.OK, legacy.StatusCode);
        var stored = await host.QueryAsync(db => db.SavedViews.AsNoTracking().SingleAsync(v => v.Name == "Legacy"));
        Assert.Equal("""{"priority":"high"}""", stored.Filters);
        Assert.True(stored.IsShared);
    }
}
