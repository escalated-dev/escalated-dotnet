using System.Net;
using System.Text.Json;
using Escalated.Authorization;
using Escalated.Enums;
using Escalated.Models;
using Escalated.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Escalated.Tests.Hosting.InertiaTestSupport;

namespace Escalated.Tests.Hosting;

/// <summary>
/// What the agent ticket page needs beyond the ticket itself: the agents it can
/// be reassigned to, the requester's other tickets, and snoozing from the agent
/// panel (escalated-laravel's AgentWorkspaceTest). Until now only the admin
/// panel could snooze.
/// </summary>
public class AgentWorkspaceTests
{
    private const string Agent = "agent-1";
    private const string OtherAgent = "agent-2";
    private const string Admin = "admin-1";
    private const string Customer = "customer-1";
    private const string Index = "/support/agent/tickets";

    private static int _reference;

    private sealed class Directory : IUserDirectory
    {
        private static readonly Dictionary<string, UserDirectoryEntry> Users = new()
        {
            [Agent] = new(Agent, "Ada Agent", "ada@example.com"),
            [OtherAgent] = new(OtherAgent, "Otto Other", "otto@example.com"),
            [Admin] = new(Admin, "Al Admin", "al@example.com"),
            [Customer] = new(Customer, "Cass Customer", "cass@example.com"),
        };

        public Task<UserDirectoryPage> ListAsync(string? search, int page, int pageSize, CancellationToken ct = default) =>
            Task.FromResult(new UserDirectoryPage(Users.Values.ToList(), Users.Count, page, pageSize));

        public Task<UserDirectoryEntry?> FindAsync(string id, CancellationToken ct = default) =>
            Task.FromResult(Users.GetValueOrDefault(id));
    }

    private static async Task<(EscalatedTestHost Host, HttpClient Client)> StartAsync(string? visibility = null, bool otherAgent = false)
    {
        var host = await EscalatedTestHost.StartAsync(options =>
        {
            options.Ui.Enabled = true;
            options.Ui.AgentInboxCacheSeconds = 0;
            if (visibility is not null) options.Visibility = visibility;
        }, services => services.AddSingleton<IUserDirectory, Directory>());
        await host.GrantRoleAsync(Agent, "escalated-agent");
        await host.GrantRoleAsync(Admin, "escalated-admin");
        if (otherAgent) await host.GrantRoleAsync(OtherAgent, "escalated-agent");
        return (host, host.ClientFor(Agent));
    }

    private static Ticket T(Action<Ticket>? customise = null)
    {
        var ticket = new Ticket
        {
            Reference = $"ESC-{Interlocked.Increment(ref _reference) + 5000}",
            Subject = "Ticket",
            Priority = TicketPriority.Low,
            Status = TicketStatus.Open,
        };
        customise?.Invoke(ticket);
        return ticket;
    }

    private static Action<Ticket> From(string requester) => t =>
    {
        t.RequesterType = "User";
        t.RequesterId = requester;
    };

    private static Task SeedAsync(EscalatedTestHost host, params Ticket[] tickets) => host.SeedAsync(async db =>
    {
        db.Tickets.AddRange(tickets);
        await db.SaveChangesAsync();
    });

    private static async Task<JsonElement> ShowAsync(HttpClient client, Ticket ticket) =>
        (await PageAsync(client, $"{Index}/{ticket.Reference}")).GetProperty("props");

    private static List<string?> Subjects(JsonElement props) =>
        props.GetProperty("ticket").GetProperty("requester_tickets").EnumerateArray()
            .Select(t => t.GetProperty("subject").GetString()).ToList();

    private static Task<Ticket> FreshAsync(EscalatedTestHost host, int id) =>
        host.QueryAsync(db => db.Tickets.IgnoreQueryFilters().AsNoTracking().SingleAsync(t => t.Id == id));

    // --- Reassigning ------------------------------------------------------------

    [Fact]
    public async Task SendsTheAgentsAndAdminsATicketCanBeAssignedTo_ByIdAndName()
    {
        var (host, client) = await StartAsync();
        await using var __ = host;
        var ticket = T();
        await SeedAsync(host, ticket);

        var agents = (await ShowAsync(client, ticket)).GetProperty("agents").EnumerateArray().ToList();

        Assert.Equal(["Ada Agent", "Al Admin"], agents.Select(a => a.GetProperty("name").GetString()));
        Assert.Equal([Agent, Admin], agents.Select(a => a.GetProperty("id").GetString()));
        Assert.All(agents, a => Assert.Equal(["id", "name"], a.EnumerateObject().Select(p => p.Name)));
    }

    [Fact]
    public async Task LeavesOutAgentsWhoCannotSeeTheTicketUnderDepartmentVisibility()
    {
        var (host, client) = await StartAsync("department", otherAgent: true);
        await using var __ = host;
        Ticket ticket = null!;
        await host.SeedAsync(async db =>
        {
            var department = new Department { Name = "Support", Slug = "support", IsActive = true };
            db.Departments.Add(department);
            await db.SaveChangesAsync();
            db.DepartmentAgents.Add(new DepartmentAgent { DepartmentId = department.Id, AgentId = Agent });
            ticket = T(t => t.DepartmentId = department.Id);
            db.Tickets.Add(ticket);
            await db.SaveChangesAsync();
        });

        var names = (await ShowAsync(client, ticket)).GetProperty("agents").EnumerateArray()
            .Select(a => a.GetProperty("name").GetString()).ToList();

        Assert.Contains("Ada Agent", names);
        Assert.DoesNotContain("Otto Other", names);
    }

    // --- The requester's other tickets ------------------------------------------

    [Fact]
    public async Task SendsTheRequestersOtherTickets_NewestFirst_AtMostFive()
    {
        var (host, client) = await StartAsync();
        await using var __ = host;
        var now = DateTime.UtcNow;
        var older = Enumerable.Range(1, 6)
            .Select(i => T(t => { From(Customer)(t); t.Subject = $"Earlier {i}"; t.CreatedAt = now.AddDays(-(10 - i)); }))
            .ToArray();
        var ticket = T(t => { From(Customer)(t); t.CreatedAt = now; });
        await SeedAsync(host, [.. older, T(t => { From("someone-else")(t); t.Subject = "Someone else"; }), ticket]);

        var props = await ShowAsync(client, ticket);
        var recent = props.GetProperty("ticket").GetProperty("requester_tickets");

        Assert.Equal(["Earlier 6", "Earlier 5", "Earlier 4", "Earlier 3", "Earlier 2"], Subjects(props));
        Assert.Equal(["id", "reference", "subject", "status", "created_at", "updated_at"],
            recent[0].EnumerateObject().Select(p => p.Name));
        Assert.Equal(older[5].Reference, recent[0].GetProperty("reference").GetString());
        Assert.Equal(older[5].Id, recent[0].GetProperty("id").GetInt32());
        Assert.Equal("open", recent[0].GetProperty("status").GetString());
        Assert.True(DateTimeOffset.TryParse(recent[0].GetProperty("created_at").GetString(), out _));
    }

    [Fact]
    public async Task MatchesAGuestRequestersOtherTicketsByEmail()
    {
        var (host, client) = await StartAsync();
        await using var __ = host;
        var ticket = T(t => { t.GuestEmail = "guest@example.com"; t.GuestName = "Gus"; t.GuestToken = "g1"; });
        await SeedAsync(host,
            T(t => { t.GuestEmail = "guest@example.com"; t.GuestName = "Gus"; t.GuestToken = "g2"; t.Subject = "Earlier guest ticket"; }),
            T(t => { t.GuestEmail = "other@example.com"; t.GuestToken = "g3"; t.Subject = "Another guest"; }),
            ticket);

        Assert.Equal(["Earlier guest ticket"], Subjects(await ShowAsync(client, ticket)));
    }

    [Fact]
    public async Task OnlyListsRequesterTicketsTheAgentMaySee()
    {
        var (host, client) = await StartAsync("department");
        await using var __ = host;
        Ticket ticket = null!;
        await host.SeedAsync(async db =>
        {
            var support = new Department { Name = "Support", Slug = "support", IsActive = true };
            var billing = new Department { Name = "Billing", Slug = "billing", IsActive = true };
            db.Departments.AddRange(support, billing);
            await db.SaveChangesAsync();
            db.DepartmentAgents.Add(new DepartmentAgent { DepartmentId = support.Id, AgentId = Agent });
            ticket = T(t => { From(Customer)(t); t.DepartmentId = support.Id; });
            db.Tickets.AddRange(
                T(t => { From(Customer)(t); t.DepartmentId = support.Id; t.Subject = "Visible"; }),
                T(t => { From(Customer)(t); t.DepartmentId = billing.Id; t.Subject = "Hidden"; }),
                ticket);
            await db.SaveChangesAsync();
        });

        Assert.Equal(["Visible"], Subjects(await ShowAsync(client, ticket)));
    }

    [Fact]
    public async Task SendsAnEmptyListForATicketWithNoKnownRequester()
    {
        var (host, client) = await StartAsync();
        await using var __ = host;
        var ticket = T();
        await SeedAsync(host, T(), ticket);

        Assert.Empty(Subjects(await ShowAsync(client, ticket)));
    }

    // --- Snoozing from the agent panel --------------------------------------------

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, Ticket ticket, string action, object body) =>
        client.SendAsync(Visit(HttpMethod.Post, $"{Index}/{ticket.Reference}/{action}", body));

    [Fact]
    public async Task LetsAnAgentSnoozeATicket()
    {
        var (host, client) = await StartAsync();
        await using var __ = host;
        var ticket = T();
        await SeedAsync(host, ticket);
        var until = DateTime.UtcNow.AddDays(1);

        var response = await PostAsync(client, ticket, "snooze", new { snoozed_until = until.ToString("O") });

        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        Assert.Equal($"{Index}/{ticket.Reference}", response.Headers.Location!.ToString());
        var page = await PageAsync(client, $"{Index}/{ticket.Reference}", FlashCookie(response));
        Assert.StartsWith("Ticket snoozed until", page.GetProperty("props").GetProperty("flash").GetProperty("success").GetString());

        var snoozed = await FreshAsync(host, ticket.Id);
        Assert.True(Math.Abs((snoozed.SnoozedUntil!.Value - until).TotalSeconds) < 1);
        Assert.True(await host.QueryAsync(db => db.TicketActivities.AnyAsync(a => a.TicketId == ticket.Id && a.Type == ActivityType.Snoozed && a.CauserId == Agent)));
    }

    [Theory]
    [InlineData("not a date")]
    [InlineData("2001-01-01T00:00:00Z")]
    [InlineData("")]
    public async Task RejectsAMissingBadOrPastSnoozeTime(string value)
    {
        var (host, client) = await StartAsync();
        await using var __ = host;
        var ticket = T();
        await SeedAsync(host, ticket);

        var response = await PostAsync(client, ticket, "snooze", new { snoozed_until = value });

        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        var page = await PageAsync(client, $"{Index}/{ticket.Reference}", FlashCookie(response));
        Assert.True(page.GetProperty("props").GetProperty("errors").TryGetProperty("snoozed_until", out _));
        Assert.Null((await FreshAsync(host, ticket.Id)).SnoozedUntil);
    }

    [Fact]
    public async Task LetsAnAgentUnsnoozeATicket()
    {
        var (host, client) = await StartAsync();
        await using var __ = host;
        var ticket = T(t => t.Status = TicketStatus.InProgress);
        await SeedAsync(host, ticket);
        await PostAsync(client, ticket, "snooze", new { snoozed_until = DateTime.UtcNow.AddDays(1).ToString("O") });

        var response = await PostAsync(client, ticket, "unsnooze", new { });

        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        var page = await PageAsync(client, $"{Index}/{ticket.Reference}", FlashCookie(response));
        Assert.Equal("Ticket unsnoozed.", page.GetProperty("props").GetProperty("flash").GetProperty("success").GetString());
        var fresh = await FreshAsync(host, ticket.Id);
        Assert.Null(fresh.SnoozedUntil);
        Assert.Equal(TicketStatus.InProgress, fresh.Status);
    }

    [Fact]
    public async Task DoesNotLetACustomerSnoozeThroughTheAgentPanel()
    {
        var (host, _) = await StartAsync();
        await using var __ = host;
        var ticket = T(From(Customer));
        await SeedAsync(host, ticket);

        var response = await PostAsync(host.ClientFor(Customer), ticket, "snooze", new { snoozed_until = DateTime.UtcNow.AddDays(1).ToString("O") });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null((await FreshAsync(host, ticket.Id)).SnoozedUntil);
    }

    [Fact]
    public async Task DoesNotLetAnAgentSnoozeATicketOutsideTheirDepartments()
    {
        var (host, client) = await StartAsync("department");
        await using var __ = host;
        Ticket ticket = null!;
        await host.SeedAsync(async db =>
        {
            var billing = new Department { Name = "Billing", Slug = "billing", IsActive = true };
            db.Departments.Add(billing);
            await db.SaveChangesAsync();
            ticket = T(t => t.DepartmentId = billing.Id);
            db.Tickets.Add(ticket);
            await db.SaveChangesAsync();
        });

        var response = await PostAsync(client, ticket, "snooze", new { snoozed_until = DateTime.UtcNow.AddDays(1).ToString("O") });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null((await FreshAsync(host, ticket.Id)).SnoozedUntil);
    }

    [Theory]
    [InlineData("snooze")]
    [InlineData("unsnooze")]
    public void MapsTheAgentSnoozeRoutesToTheTicketEditPermission(string action)
    {
        Assert.Equal(["ticket.edit"], RoutePermissions.Mapped("POST", $"/P/agent/tickets/{{reference}}/{action}"));
    }

    [Fact]
    public async Task WithRolesAndPermissionsOn_SnoozingNeedsTicketEdit()
    {
        await using var host = await EscalatedTestHost.StartAsync(options =>
        {
            options.Ui.Enabled = true;
            options.Ui.AgentInboxCacheSeconds = 0;
            options.Access.RolesPermissions = true;
        }, services => services.AddSingleton<IUserDirectory, Directory>());
        using (var scope = host.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<PermissionSeeder>().SeedAsync();
        }

        await host.GrantRoleAsync(Agent, "escalated-agent");
        await host.GrantRoleAsync(Agent, "light_agent");
        await host.GrantRoleAsync(OtherAgent, "escalated-agent");
        await host.GrantRoleAsync(OtherAgent, "agent");
        var ticket = T();
        await SeedAsync(host, ticket);
        var body = new { snoozed_until = DateTime.UtcNow.AddDays(1).ToString("O") };

        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(host.ClientFor(Agent), ticket, "snooze", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(host.ClientFor(Agent), ticket, "unsnooze", new { })).StatusCode);
        Assert.Null((await FreshAsync(host, ticket.Id)).SnoozedUntil);

        Assert.Equal(HttpStatusCode.SeeOther, (await PostAsync(host.ClientFor(OtherAgent), ticket, "snooze", body)).StatusCode);
        Assert.NotNull((await FreshAsync(host, ticket.Id)).SnoozedUntil);
    }
}
