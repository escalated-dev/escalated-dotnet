using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Escalated.Enums;
using Escalated.Models;
using Escalated.Services;
using Escalated.Services.Email.Inbound;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Escalated.Tests.Hosting.InertiaTestSupport;

namespace Escalated.Tests.Hosting;

/// <summary>
/// The agent pages and their form posts, against the props escalated-laravel's
/// <c>Agent/DashboardController</c> and <c>Agent/TicketController</c> render and
/// the keys the shared Vue pages read (snake_case, nested relations included).
/// </summary>
public class AgentPagesTests
{
    private const string Agent = "agent-1";
    private const string OtherAgent = "agent-2";
    private const string Customer = "customer-1";

    private sealed class Directory : IUserDirectory
    {
        private static readonly Dictionary<string, UserDirectoryEntry> Users = new()
        {
            [Agent] = new(Agent, "Avery Agent", "avery@example.com"),
            [OtherAgent] = new(OtherAgent, "Blake Agent", "blake@example.com"),
            [Customer] = new(Customer, "Casey Customer", "casey@example.com"),
        };

        public Task<UserDirectoryPage> ListAsync(string? search, int page, int pageSize, CancellationToken ct = default) =>
            Task.FromResult(new UserDirectoryPage(Users.Values.ToList(), Users.Count, page, pageSize));

        public Task<UserDirectoryEntry?> FindAsync(string id, CancellationToken ct = default) =>
            Task.FromResult(Users.GetValueOrDefault(id));
    }

    private sealed class MemoryStorage : IAttachmentStorage
    {
        public string Name => "memory";

        public Task<string> PutAsync(string filename, Stream content, string contentType, CancellationToken ct = default) =>
            Task.FromResult($"memory/{Guid.NewGuid():N}/{filename}");
    }

    private sealed record Seeded(int TicketId, int DepartmentId, int OtherDepartmentId, int TagId, int OtherTagId, int MacroId);

    private static async Task<(EscalatedTestHost Host, HttpClient Client, Seeded Seed)> StartAsync()
    {
        var host = await EscalatedTestHost.StartAsync(
            options => options.Ui.Enabled = true,
            services =>
            {
                services.AddSingleton<IUserDirectory, Directory>();
                services.AddSingleton<IAttachmentStorage, MemoryStorage>();
            });
        await host.GrantRoleAsync(Agent, "escalated-agent");

        Seeded? seeded = null;
        await host.SeedAsync(async db =>
        {
            var billing = new Department { Name = "Billing", Slug = "billing", IsActive = true };
            var support = new Department { Name = "Support", Slug = "support", IsActive = true };
            var urgentTag = new Tag { Name = "VIP", Slug = "vip", Color = "#ff0000" };
            var otherTag = new Tag { Name = "Refund", Slug = "refund", Color = "#00ff00" };
            db.AddRange(billing, support, urgentTag, otherTag);
            await db.SaveChangesAsync();

            var ticket = new Ticket
            {
                Reference = "ESC-00001",
                Subject = "Card declined",
                Description = "My card was declined twice.",
                Priority = TicketPriority.Urgent,
                RequesterType = "User",
                RequesterId = Customer,
                DepartmentId = billing.Id,
                SlaFirstResponseBreached = true,
                CreatedAt = DateTime.UtcNow.AddHours(-2),
            };
            var assigned = new Ticket
            {
                Reference = "ESC-00002",
                Subject = "Invoice copy",
                AssignedTo = Agent,
                Status = TicketStatus.InProgress,
                GuestName = "Gina Guest",
                GuestEmail = "gina@example.com",
                GuestToken = "token",
                CreatedAt = DateTime.UtcNow.AddHours(-1),
            };
            var resolved = new Ticket
            {
                Reference = "ESC-00003",
                Subject = "Fixed already",
                AssignedTo = Agent,
                Status = TicketStatus.Resolved,
                ResolvedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow.AddHours(-3),
            };
            db.Tickets.AddRange(ticket, assigned, resolved);
            await db.SaveChangesAsync();

            db.TicketTags.Add(new TicketTag { TicketId = ticket.Id, TagId = urgentTag.Id });
            db.Replies.AddRange(
                new Reply { TicketId = ticket.Id, Body = "Please retry.", AuthorId = Agent, CreatedAt = DateTime.UtcNow.AddMinutes(-30) },
                new Reply { TicketId = ticket.Id, Body = "Checked the gateway.", AuthorId = Agent, IsInternalNote = true, IsPinned = true, Type = "note", CreatedAt = DateTime.UtcNow.AddMinutes(-20) });
            db.TicketActivities.Add(new TicketActivity { TicketId = ticket.Id, Type = ActivityType.PriorityChanged, CauserId = Agent, Properties = """{"old":"medium","new":"urgent"}""" });
            db.SatisfactionRatings.Add(new SatisfactionRating { TicketId = resolved.Id, Rating = 5, Comment = "Great" });
            db.CannedResponses.AddRange(
                new CannedResponse { Title = "Thanks", Body = "Thanks for writing.", IsShared = true },
                new CannedResponse { Title = "Private", Body = "Someone else's.", CreatedBy = OtherAgent, IsShared = false });
            var macro = new Macro { Name = "Escalate", Description = "Bump it", Actions = """[{"type":"priority","value":"high"}]""", IsShared = true };
            db.Macros.AddRange(macro, new Macro { Name = "Hidden", Actions = "[]", CreatedBy = OtherAgent, IsShared = false });
            db.TicketFollowers.Add(new TicketFollower { TicketId = ticket.Id, UserId = OtherAgent });
            await db.SaveChangesAsync();

            seeded = new Seeded(ticket.Id, billing.Id, support.Id, urgentTag.Id, otherTag.Id, macro.Id);
        });

        return (host, host.ClientFor(Agent), seeded!);
    }

    [Fact]
    public async Task Dashboard_RendersStatsAndTicketsAsThePageReadsThem()
    {
        var (host, client, _) = await StartAsync();
        await using var __ = host;

        var page = await PageAsync(client, "/support/agent");
        AssertPage(page, "Escalated/Agent/Dashboard");
        var props = page.GetProperty("props");

        var stats = Keys(props.GetProperty("stats"), "open", "my_assigned", "unassigned", "sla_breached", "resolved_today");
        Assert.Equal(2, stats.GetProperty("open").GetInt32());
        Assert.Equal(1, stats.GetProperty("my_assigned").GetInt32());
        Assert.Equal(1, stats.GetProperty("unassigned").GetInt32());
        Assert.Equal(1, stats.GetProperty("sla_breached").GetInt32());
        Assert.Equal(1, stats.GetProperty("resolved_today").GetInt32());

        var recent = props.GetProperty("recentTickets");
        Assert.Equal(3, recent.GetArrayLength());
        var newest = Keys(recent[0], "id", "reference", "subject", "status", "priority", "requester", "assignee", "latest_reply");
        Assert.Equal("ESC-00002", newest.GetProperty("reference").GetString());
        Assert.Equal("in_progress", newest.GetProperty("status").GetString());
        Assert.Equal("Gina Guest", newest.GetProperty("requester").GetProperty("name").GetString());
        Assert.Equal("Avery Agent", newest.GetProperty("assignee").GetProperty("name").GetString());

        var declined = recent.EnumerateArray().Single(t => t.GetProperty("reference").GetString() == "ESC-00001");
        Assert.Equal("Casey Customer", declined.GetProperty("requester").GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, declined.GetProperty("assignee").ValueKind);
        var latest = Keys(declined.GetProperty("latest_reply"), "created_at", "author");
        Assert.Equal("Avery Agent", latest.GetProperty("author").GetProperty("name").GetString());
        Assert.EndsWith("Z", latest.GetProperty("created_at").GetString());

        var attention = Keys(props.GetProperty("needsAttention"), "sla_breaching", "unassigned_urgent");
        Assert.Equal("ESC-00001", attention.GetProperty("sla_breaching")[0].GetProperty("reference").GetString());
        Assert.Equal("urgent", attention.GetProperty("unassigned_urgent")[0].GetProperty("priority").GetString());

        Assert.Equal(1, props.GetProperty("myPerformance").GetProperty("resolved_this_week").GetInt32());
    }

    [Fact]
    public async Task TicketIndex_RendersAPaginatorOfRowsAndTheFilterOptions()
    {
        var (host, client, seed) = await StartAsync();
        await using var __ = host;

        var page = await PageAsync(client, "/support/agent/tickets?status=open&search=Card");
        AssertPage(page, "Escalated/Agent/TicketIndex");
        var props = page.GetProperty("props");

        var tickets = Keys(props.GetProperty("tickets"), "data", "current_page", "last_page", "per_page", "total", "links");
        Assert.Equal(1, tickets.GetProperty("total").GetInt32());
        var row = Keys(tickets.GetProperty("data")[0], "id", "reference", "subject", "status", "priority", "ticket_type",
            "requester", "requester_name", "requester_email", "assignee", "department", "tags", "latest_reply",
            "last_reply_at", "last_reply_author", "created_at", "first_response_due_at", "resolution_due_at",
            "sla_first_response_breached", "sla_resolution_breached", "snoozed_until");
        Assert.Equal("ESC-00001", row.GetProperty("reference").GetString());
        Assert.Equal("Billing", row.GetProperty("department").GetProperty("name").GetString());
        Assert.Equal("VIP", row.GetProperty("tags")[0].GetProperty("name").GetString());
        Assert.Equal("casey@example.com", row.GetProperty("requester_email").GetString());

        Assert.Equal("open", props.GetProperty("filters").GetProperty("status").GetString());
        Assert.Equal("Card", props.GetProperty("filters").GetProperty("search").GetString());
        Keys(props.GetProperty("departments")[0], "id", "name");
        Keys(props.GetProperty("tags")[0], "id", "name", "color");

        var mine = await PageAsync(client, "/support/agent/tickets?assigned_to=" + Agent);
        Assert.Equal(2, mine.GetProperty("props").GetProperty("tickets").GetProperty("total").GetInt32());

        var tagged = await PageAsync(client, $"/support/agent/tickets?tag_ids[]={seed.TagId}");
        Assert.Equal(1, tagged.GetProperty("props").GetProperty("tickets").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task TicketShow_RendersTheTicketWithItsRelations()
    {
        var (host, client, _) = await StartAsync();
        await using var __ = host;

        var page = await PageAsync(client, "/support/agent/tickets/ESC-00001");
        AssertPage(page, "Escalated/Agent/TicketShow");
        var props = page.GetProperty("props");

        var ticket = Keys(props.GetProperty("ticket"), "id", "reference", "subject", "description", "status", "priority",
            "ticket_type", "assigned_to", "requester", "department", "tags", "replies", "pinned_notes", "attachments",
            "activities", "satisfaction_rating", "related_tickets", "requester_ticket_count", "subjects",
            "chat_session_id", "chat_messages", "chat_metadata", "sla_first_response_breached", "first_response_due_at",
            "resolution_due_at", "created_at");
        Assert.Equal("Card declined", ticket.GetProperty("subject").GetString());
        Assert.Equal("Casey Customer", ticket.GetProperty("requester").GetProperty("name").GetString());
        Assert.Equal("Billing", ticket.GetProperty("department").GetProperty("name").GetString());
        Assert.Equal(1, ticket.GetProperty("requester_ticket_count").GetInt32());

        var replies = ticket.GetProperty("replies");
        Assert.Equal(2, replies.GetArrayLength());
        var note = Keys(replies[0], "id", "body", "is_internal_note", "is_pinned", "author", "attachments", "created_at");
        Assert.True(note.GetProperty("is_internal_note").GetBoolean());
        Assert.Equal("Avery Agent", note.GetProperty("author").GetProperty("name").GetString());
        Assert.Equal("Checked the gateway.", ticket.GetProperty("pinned_notes")[0].GetProperty("body").GetString());

        var activity = Keys(ticket.GetProperty("activities")[0], "id", "type", "description", "properties", "causer", "created_at", "created_at_human");
        Assert.Equal("priority_changed", activity.GetProperty("type").GetString());
        Assert.Equal("urgent", activity.GetProperty("properties").GetProperty("new").GetString());

        var canned = props.GetProperty("cannedResponses");
        Assert.Equal("Thanks", Assert.Single(canned.EnumerateArray()).GetProperty("title").GetString());
        var macro = Assert.Single(props.GetProperty("macros").EnumerateArray());
        Assert.Equal("Escalate", macro.GetProperty("name").GetString());
        Assert.False(props.GetProperty("is_following").GetBoolean());
        Assert.Equal(1, props.GetProperty("followers_count").GetInt32());
        Assert.Equal(JsonValueKind.Array, props.GetProperty("customActions").ValueKind);
    }

    [Fact]
    public async Task TicketShow_IsA404ForAnUnknownReference()
    {
        var (host, client, _) = await StartAsync();
        await using var __ = host;

        var response = await client.SendAsync(Visit(HttpMethod.Get, "/support/agent/tickets/ESC-99999"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string action, object body)
    {
        var response = await client.SendAsync(Visit(HttpMethod.Post, $"/support/agent/tickets/ESC-00001/{action}", body));
        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        Assert.Equal("/support/agent/tickets/ESC-00001", response.Headers.Location!.ToString());
        return response;
    }

    private static Task<Ticket> TicketAsync(EscalatedTestHost host) =>
        host.QueryAsync(db => db.Tickets.AsNoTracking().SingleAsync(t => t.Reference == "ESC-00001"));

    [Fact]
    public async Task ReplyAndNote_AddToTheThread()
    {
        var (host, client, seed) = await StartAsync();
        await using var __ = host;

        await PostAsync(client, "reply", new { body = "We refunded you." });
        await PostAsync(client, "note", new { body = "Refund id 42" });
        await PostAsync(client, "reply", new { body = "Composer note", is_internal_note = true });

        var replies = await host.QueryAsync(db => db.Replies.AsNoTracking()
            .Where(r => r.TicketId == seed.TicketId).OrderBy(r => r.Id).Skip(2).ToListAsync());
        Assert.Equal(new[] { "We refunded you.", "Refund id 42", "Composer note" }, replies.Select(r => r.Body));
        Assert.Equal(new[] { false, true, true }, replies.Select(r => r.IsInternalNote));
        Assert.All(replies, r => Assert.Equal(Agent, r.AuthorId));
    }

    [Fact]
    public async Task AReplyWithFiles_StoresThemOnTheReply()
    {
        var (host, client, seed) = await StartAsync();
        await using var __ = host;

        var form = new MultipartFormDataContent
        {
            { new StringContent("See attached."), "body" },
        };
        var file = new ByteArrayContent("hello"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(file, "attachments[0]", "notes.txt");
        var request = Visit(HttpMethod.Post, "/support/agent/tickets/ESC-00001/reply");
        request.Content = form;

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        var attachment = await host.QueryAsync(db => db.Attachments.AsNoTracking().SingleAsync());
        Assert.Equal("reply", attachment.AttachableType);
        Assert.Equal("notes.txt", attachment.Filename);
        var reply = await host.QueryAsync(db => db.Replies.AsNoTracking().SingleAsync(r => r.Id == attachment.AttachableId));
        Assert.Equal("See attached.", reply.Body);

        var page = await PageAsync(client, "/support/agent/tickets/ESC-00001");
        var shown = page.GetProperty("props").GetProperty("ticket").GetProperty("replies")[0].GetProperty("attachments")[0];
        Keys(shown, "id", "original_filename", "mime_type", "size", "url");
    }

    [Fact]
    public async Task StatusPriorityAssignDepartmentAndTags_ChangeTheTicket()
    {
        var (host, client, seed) = await StartAsync();
        await using var __ = host;

        await PostAsync(client, "status", new { status = "in_progress" });
        await PostAsync(client, "priority", new { priority = "low" });
        await PostAsync(client, "assign", new { agent_id = OtherAgent });
        await PostAsync(client, "department", new { department_id = seed.OtherDepartmentId });
        await PostAsync(client, "tags", new { tag_ids = new[] { seed.OtherTagId } });

        var ticket = await TicketAsync(host);
        Assert.Equal(TicketStatus.InProgress, ticket.Status);
        Assert.Equal(TicketPriority.Low, ticket.Priority);
        Assert.Equal(OtherAgent, ticket.AssignedTo);
        Assert.Equal(seed.OtherDepartmentId, ticket.DepartmentId);
        var tags = await host.QueryAsync(db => db.TicketTags.Where(tt => tt.TicketId == seed.TicketId).Select(tt => tt.TagId).ToListAsync());
        Assert.Equal(new[] { seed.OtherTagId }, tags);
    }

    [Theory]
    [InlineData("status", "status", "nope")]
    [InlineData("priority", "priority", "extreme")]
    [InlineData("department", "department_id", "999")]
    [InlineData("assign", "agent_id", "")]
    [InlineData("tags", "tag_ids", "999")]
    [InlineData("macro", "macro_id", "999")]
    public async Task InvalidInput_ComesBackAsAnError(string action, string field, string value)
    {
        var (host, client, _) = await StartAsync();
        await using var __ = host;

        object body = field == "tag_ids" ? new Dictionary<string, object> { [field] = new[] { value } } : new Dictionary<string, object> { [field] = value };
        var response = await PostAsync(client, action, body);

        var page = await PageAsync(client, "/support/agent/tickets/ESC-00001", FlashCookie(response));
        Assert.True(page.GetProperty("props").GetProperty("errors").TryGetProperty(field, out _));

        var ticket = await TicketAsync(host);
        Assert.Equal(TicketPriority.Urgent, ticket.Priority);
        Assert.Equal(TicketStatus.Open, ticket.Status);
    }

    [Fact]
    public async Task MacroAndFollow_Work()
    {
        var (host, client, seed) = await StartAsync();
        await using var __ = host;

        await PostAsync(client, "macro", new { macro_id = seed.MacroId });
        Assert.Equal(TicketPriority.High, (await TicketAsync(host)).Priority);

        await PostAsync(client, "follow", new { });
        var page = await PageAsync(client, "/support/agent/tickets/ESC-00001");
        Assert.True(page.GetProperty("props").GetProperty("is_following").GetBoolean());
        Assert.Equal(2, page.GetProperty("props").GetProperty("followers_count").GetInt32());

        await PostAsync(client, "follow", new { });
        Assert.False(await host.QueryAsync(db => db.TicketFollowers.AnyAsync(f => f.UserId == Agent)));
    }

    [Fact]
    public async Task Bulk_ChangesEveryChosenTicket()
    {
        var (host, client, seed) = await StartAsync();
        await using var __ = host;
        var ids = await host.QueryAsync(db => db.Tickets.Where(t => t.Status != TicketStatus.Resolved).Select(t => t.Id).ToListAsync());

        var response = await client.SendAsync(Visit(HttpMethod.Post, "/support/agent/tickets/bulk",
            new { ticket_ids = ids, action = "priority", value = "critical" }));

        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        var priorities = await host.QueryAsync(db => db.Tickets.Where(t => ids.Contains(t.Id)).Select(t => t.Priority).ToListAsync());
        Assert.All(priorities, p => Assert.Equal(TicketPriority.Critical, p));

        // The JSON API's bulk route still answers JSON callers.
        var api = await client.PostAsync("/support/agent/tickets/bulk",
            JsonContent.Create(new { ticketIds = ids, action = "close" }));
        Assert.Equal(HttpStatusCode.OK, api.StatusCode);
    }
}
