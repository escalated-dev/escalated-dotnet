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
/// The customer pages and their form posts, against escalated-laravel's
/// <c>Customer/TicketController</c>, <c>SatisfactionRatingController</c> and
/// <c>CustomerTicketPayload</c>: own tickets only, public replies only, staff by
/// display name only.
/// </summary>
public class CustomerPagesTests
{
    private const string Customer = "customer-1";
    private const string OtherCustomer = "customer-2";
    private const string Agent = "agent-1";

    private sealed class Directory : IUserDirectory
    {
        private static readonly Dictionary<string, UserDirectoryEntry> Users = new()
        {
            [Customer] = new(Customer, "Casey Customer", "casey@example.com"),
            [Agent] = new(Agent, "Avery Agent", "avery@example.com"),
        };

        public Task<UserDirectoryPage> ListAsync(string? search, int page, int pageSize, CancellationToken ct = default) =>
            Task.FromResult(new UserDirectoryPage(Users.Values.ToList(), Users.Count, page, pageSize));

        public Task<UserDirectoryEntry?> FindAsync(string id, CancellationToken ct = default) =>
            Task.FromResult(Users.GetValueOrDefault(id));
    }

    private static async Task<(EscalatedTestHost Host, HttpClient Client, int DepartmentId)> StartAsync(bool allowClose = true)
    {
        var host = await EscalatedTestHost.StartAsync(
            options =>
            {
                options.Ui.Enabled = true;
                options.AllowCustomerClose = allowClose;
            },
            services => services.AddSingleton<IUserDirectory, Directory>());

        var departmentId = 0;
        await host.SeedAsync(async db =>
        {
            var billing = new Department { Name = "Billing", Slug = "billing", IsActive = true };
            db.Departments.AddRange(billing, new Department { Name = "Closed dept", Slug = "closed", IsActive = false });
            await db.SaveChangesAsync();
            departmentId = billing.Id;

            var mine = new Ticket { Reference = "ESC-00001", Subject = "Card declined", Description = "Twice.", RequesterId = Customer, DepartmentId = billing.Id, AssignedTo = Agent };
            var resolved = new Ticket { Reference = "ESC-00002", Subject = "Old issue", RequesterId = Customer, Status = TicketStatus.Resolved, ResolvedAt = DateTime.UtcNow };
            var theirs = new Ticket { Reference = "ESC-00003", Subject = "Not yours", RequesterId = OtherCustomer };
            db.Tickets.AddRange(mine, resolved, theirs);
            await db.SaveChangesAsync();

            db.Replies.AddRange(
                new Reply { TicketId = mine.Id, Body = "Card declined, help", AuthorId = Customer, CreatedAt = DateTime.UtcNow.AddMinutes(-30) },
                new Reply { TicketId = mine.Id, Body = "Please retry.", AuthorId = Agent, CreatedAt = DateTime.UtcNow.AddMinutes(-20) },
                new Reply { TicketId = mine.Id, Body = "Internal: suspected fraud", AuthorId = Agent, IsInternalNote = true, CreatedAt = DateTime.UtcNow.AddMinutes(-10) });
            await db.SaveChangesAsync();
        });

        return (host, host.ClientFor(Customer), departmentId);
    }

    [Fact]
    public async Task Index_ListsOnlyTheCustomersTickets()
    {
        var (host, client, _) = await StartAsync();
        await using var hostScope = host;

        var page = await PageAsync(client, "/support?status=open");
        AssertPage(page, "Escalated/Customer/Index");
        var props = page.GetProperty("props");

        var tickets = Keys(props.GetProperty("tickets"), "data", "current_page", "last_page", "total", "links");
        var row = Keys(Assert.Single(tickets.GetProperty("data").EnumerateArray()),
            "reference", "subject", "status", "priority", "department", "created_at", "updated_at", "last_reply_at");
        Assert.Equal("ESC-00001", row.GetProperty("reference").GetString());
        Assert.Equal("Billing", row.GetProperty("department").GetProperty("name").GetString());
        Assert.Equal("open", props.GetProperty("filters").GetProperty("status").GetString());

        var all = await PageAsync(client, "/support");
        var references = all.GetProperty("props").GetProperty("tickets").GetProperty("data").EnumerateArray()
            .Select(t => t.GetProperty("reference").GetString()).OrderBy(r => r).ToList();
        Assert.Equal(new[] { "ESC-00001", "ESC-00002" }, references);
    }

    [Fact]
    public async Task Create_OffersActiveDepartmentsAndPriorities()
    {
        var (host, client, _) = await StartAsync();
        await using var hostScope = host;

        var page = await PageAsync(client, "/support/create");
        AssertPage(page, "Escalated/Customer/Create");
        var props = page.GetProperty("props");

        var department = Assert.Single(props.GetProperty("departments").EnumerateArray());
        Assert.Equal("Billing", Keys(department, "id", "name").GetProperty("name").GetString());
        Assert.Equal(new[] { "low", "medium", "high", "urgent", "critical" },
            props.GetProperty("priorities").EnumerateArray().Select(p => p.GetString()));
    }

    [Fact]
    public async Task Store_OpensATicketAndRedirectsToIt()
    {
        var (host, client, departmentId) = await StartAsync();
        await using var hostScope = host;

        var response = await client.SendAsync(Visit(HttpMethod.Post, "/support", new
        {
            subject = "Need an invoice",
            description = "For October.",
            priority = "high",
            department_id = departmentId,
            ticket_type = "question",
        }));

        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        var ticket = await host.QueryAsync(db => db.Tickets.AsNoTracking().SingleAsync(t => t.Subject == "Need an invoice"));
        Assert.Equal(Customer, ticket.RequesterId);
        Assert.Equal(TicketPriority.High, ticket.Priority);
        Assert.Equal(departmentId, ticket.DepartmentId);
        Assert.Equal($"/support/{ticket.Reference}", response.Headers.Location!.ToString());

        var page = await PageAsync(client, response.Headers.Location!.ToString(), FlashCookie(response));
        Assert.Equal("Ticket created.", page.GetProperty("props").GetProperty("flash").GetProperty("success").GetString());
    }

    [Fact]
    public async Task Store_ReturnsTheErrorsForBadInput()
    {
        var (host, client, _) = await StartAsync();
        await using var hostScope = host;

        var response = await client.SendAsync(Visit(HttpMethod.Post, "/support",
            new { subject = "", description = "", priority = "extreme", department_id = 999 }));

        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        Assert.Equal("/support/create", response.Headers.Location!.ToString());
        var page = await PageAsync(client, "/support/create", FlashCookie(response));
        Keys(page.GetProperty("props").GetProperty("errors"), "subject", "description", "priority", "department_id");
        Assert.Equal(3, await host.QueryAsync(db => db.Tickets.CountAsync()));
    }

    [Fact]
    public async Task Show_HasPublicRepliesAndStaffByNameOnly()
    {
        var (host, client, _) = await StartAsync();
        await using var hostScope = host;

        var page = await PageAsync(client, "/support/ESC-00001");
        AssertPage(page, "Escalated/Customer/Show");
        var ticket = Keys(page.GetProperty("props").GetProperty("ticket"), "reference", "subject", "description", "status",
            "priority", "department", "replies", "attachments", "satisfaction_rating", "created_at");

        var replies = ticket.GetProperty("replies").EnumerateArray().ToList();
        Assert.Equal(new[] { "Please retry.", "Card declined, help" }, replies.Select(r => r.GetProperty("body").GetString()));
        var staff = Keys(replies[0].GetProperty("author"), "id", "name", "email");
        Assert.Equal("Avery Agent", staff.GetProperty("name").GetString());
        Assert.Equal("", staff.GetProperty("email").GetString());
        Assert.Equal(0, staff.GetProperty("id").GetInt32());
        Assert.Equal("casey@example.com", replies[1].GetProperty("author").GetProperty("email").GetString());
        Assert.DoesNotContain("suspected fraud", page.ToString());
        Assert.DoesNotContain("avery@example.com", page.GetProperty("props").GetProperty("ticket").ToString());
    }

    [Fact]
    public async Task AnotherCustomersTicket_IsNotFound()
    {
        var (host, client, _) = await StartAsync();
        await using var hostScope = host;

        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(Visit(HttpMethod.Get, "/support/ESC-00003"))).StatusCode);
        var reply = await client.SendAsync(Visit(HttpMethod.Post, "/support/ESC-00003/reply", new { body = "hi" }));
        Assert.Equal(HttpStatusCode.NotFound, reply.StatusCode);
        Assert.Equal(0, await host.QueryAsync(db => db.Replies.CountAsync(r => r.Body == "hi")));
    }

    [Fact]
    public async Task Reply_AddsAPublicReplyEvenWhenANoteIsAskedFor()
    {
        var (host, client, _) = await StartAsync();
        await using var hostScope = host;

        var response = await client.SendAsync(Visit(HttpMethod.Post, "/support/ESC-00001/reply",
            new { body = "Still declined", is_internal_note = true }));

        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        var reply = await host.QueryAsync(db => db.Replies.AsNoTracking().SingleAsync(r => r.Body == "Still declined"));
        Assert.False(reply.IsInternalNote);
        Assert.Equal(Customer, reply.AuthorId);
    }

    [Fact]
    public async Task CloseAndReopen_ChangeTheStatus()
    {
        var (host, client, _) = await StartAsync();
        await using var hostScope = host;

        var close = await client.SendAsync(Visit(HttpMethod.Post, "/support/ESC-00001/close", new { }));
        Assert.Equal(HttpStatusCode.SeeOther, close.StatusCode);
        Assert.Equal(TicketStatus.Closed, (await host.QueryAsync(db => db.Tickets.SingleAsync(t => t.Reference == "ESC-00001"))).Status);

        var reopen = await client.SendAsync(Visit(HttpMethod.Post, "/support/ESC-00001/reopen", new { }));
        Assert.Equal(HttpStatusCode.SeeOther, reopen.StatusCode);
        Assert.Equal(TicketStatus.Reopened, (await host.QueryAsync(db => db.Tickets.SingleAsync(t => t.Reference == "ESC-00001"))).Status);
    }

    [Fact]
    public async Task Close_IsRefusedWhenCustomersMayNotClose()
    {
        var (host, client, _) = await StartAsync(allowClose: false);
        await using var hostScope = host;

        var close = await client.SendAsync(Visit(HttpMethod.Post, "/support/ESC-00001/close", new { }));

        Assert.Equal(HttpStatusCode.Forbidden, close.StatusCode);
    }

    [Fact]
    public async Task Rate_RecordsOneRatingOnAResolvedTicket()
    {
        var (host, client, _) = await StartAsync();
        await using var hostScope = host;

        var open = await client.SendAsync(Visit(HttpMethod.Post, "/support/ESC-00001/rate", new { rating = 5 }));
        var openPage = await PageAsync(client, "/support/ESC-00001", FlashCookie(open));
        Assert.Equal("Only resolved or closed tickets can be rated.", openPage.GetProperty("props").GetProperty("flash").GetProperty("error").GetString());

        var bad = await client.SendAsync(Visit(HttpMethod.Post, "/support/ESC-00002/rate", new { rating = 9 }));
        var badPage = await PageAsync(client, "/support/ESC-00002", FlashCookie(bad));
        Assert.True(badPage.GetProperty("props").GetProperty("errors").TryGetProperty("rating", out _));

        var good = await client.SendAsync(Visit(HttpMethod.Post, "/support/ESC-00002/rate", new { rating = 4, comment = "Quick" }));
        Assert.Equal(HttpStatusCode.SeeOther, good.StatusCode);
        var again = await client.SendAsync(Visit(HttpMethod.Post, "/support/ESC-00002/rate", new { rating = 1 }));
        Assert.Equal(HttpStatusCode.SeeOther, again.StatusCode);

        var rating = await host.QueryAsync(db => db.SatisfactionRatings.AsNoTracking().SingleAsync());
        Assert.Equal(4, rating.Rating);
        Assert.Equal("Quick", rating.Comment);

        var page = await PageAsync(client, "/support/ESC-00002");
        Assert.Equal(4, page.GetProperty("props").GetProperty("ticket").GetProperty("satisfaction_rating").GetProperty("rating").GetInt32());
    }

    [Fact]
    public async Task TheCustomerApiIsUnchanged()
    {
        var (host, client, _) = await StartAsync();
        await using var hostScope = host;

        var api = await client.GetAsync("/support/tickets");
        using var json = JsonDocument.Parse(await api.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.TryGetProperty("perPage", out _));
    }
}
