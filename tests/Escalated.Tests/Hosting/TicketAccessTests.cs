using System.Net;
using System.Security.Claims;
using Escalated.Hubs;
using Microsoft.AspNetCore.SignalR;
using Moq;
using System.Net.Http.Json;
using System.Text.Json;
using Escalated.Enums;
using Escalated.Models;
using Xunit;
using Escalated.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Escalated.Authorization;
using Escalated.Configuration;
using Escalated.Data;
using Escalated.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Escalated.Tests.Hosting;

public class TicketAccessTests
{
    private static async Task<(EscalatedTestHost Host, Ticket Visible, Ticket Hidden)> StartAsync(Action<EscalatedOptions>? configure = null, Action<IServiceCollection>? services = null)
    {
        var host = await EscalatedTestHost.StartAsync(options => { options.Visibility = "department"; configure?.Invoke(options); }, services);
        await host.GrantRoleAsync("agent", "escalated-agent");
        await host.GrantRoleAsync("admin", "escalated-admin");
        var own = new Department { Name = "Own", Slug = "own" };
        var other = new Department { Name = "Other", Slug = "other" };
        var visible = new Ticket { Reference = "ACCESS-1", Subject = "Visible", Description = "Details", Department = own, TicketType = "question" };
        var hidden = new Ticket { Reference = "ACCESS-2", Subject = "Private search marker", Description = "Details", Department = other, AssignedTo = "agent", GuestToken = "private-token", TicketType = "question" };
        await host.SeedAsync(async db =>
        {
            db.Tickets.AddRange(visible, hidden);
            await db.SaveChangesAsync();
            db.DepartmentAgents.Add(new DepartmentAgent { DepartmentId = own.Id, AgentId = "agent" });
            await db.SaveChangesAsync();
        });
        return (host, visible, hidden);
    }

    [Fact]
    public async Task List_OnlyIncludesAgentDepartments()
    {
        var (host, visible, _) = await StartAsync();
        await using var lease = host;
        using var response = await host.ClientFor("agent").GetAsync("/support/agent/tickets");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var ticket = Assert.Single(json.RootElement.GetProperty("data").EnumerateArray());
        Assert.Equal(visible.Id, ticket.GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task Search_DoesNotExposePrivateMatches()
    {
        var (host, _, _) = await StartAsync();
        await using var lease = host;
        using var response = await host.ClientFor("agent").GetAsync("/support/agent/tickets?search=Private");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Empty(json.RootElement.GetProperty("data").EnumerateArray());
    }

    [Fact]
    public async Task Details_DenyAssignedTicketsOutsideDepartment()
    {
        var (host, _, hidden) = await StartAsync();
        await using var lease = host;
        using var response = await host.ClientFor("agent").GetAsync($"/support/agent/tickets/{hidden.Id}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Bulk_ChangesOnlyVisibleTickets()
    {
        var (host, visible, hidden) = await StartAsync();
        await using var lease = host;
        using var response = await host.ClientFor("agent").PostAsJsonAsync("/support/agent/tickets/bulk", new { ticketIds = new[] { visible.Id, hidden.Id }, action = "change_priority", value = "high" });
        response.EnsureSuccessStatusCode();
        var priority = await host.QueryAsync(async db => (await db.Tickets.FindAsync(hidden.Id))!.Priority);
        Assert.Equal(TicketPriority.Medium, priority);
    }

    [Fact]
    public async Task Widget_RestrictsAuthenticatedAgentsEvenWithAValidGuestToken()
    {
        var (host, _, _) = await StartAsync();
        await using var lease = host;
        using var guest = await host.Client.GetAsync("/support/widget/tickets/private-token");
        Assert.Equal(HttpStatusCode.OK, guest.StatusCode);
        using var agent = await host.ClientFor("agent").GetAsync("/support/widget/tickets/private-token");
        Assert.Equal(HttpStatusCode.NotFound, agent.StatusCode);
    }

    [Fact]
    public async Task WorkloadReport_ExcludesAssignedTicketsOutsideDepartment()
    {
        var (host, _, _) = await StartAsync();
        await using var lease = host;
        using var response = await host.ClientFor("agent").GetAsync("/support/agent/tickets/dashboard");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(0, json.RootElement.GetProperty("open").GetInt32());
    }

    [Fact]
    public async Task Accounts_IntersectDepartmentsAndUseExactIdentity()
    {
        var account = new TicketAccount("Company", "ABC");
        var (host, visible, hidden) = await StartAsync(options =>
        {
            options.Accounts.Enabled = true;
            options.Accounts.ScopeAgents = true;
            options.Accounts.Resolver = (_, _) => Task.FromResult<TicketAccount?>(account);
        });
        await using var lease = host;
        await host.SeedAsync(async db =>
        {
            foreach (var ticket in await db.Tickets.ToListAsync()) { ticket.AccountType = "Company"; ticket.AccountId = "ABC"; }
            await db.SaveChangesAsync();
        });
        using var scope = host.Services.CreateScope();
        var access = scope.ServiceProvider.GetRequiredService<TicketAccess>();
        var db = scope.ServiceProvider.GetRequiredService<EscalatedDbContext>();
        Assert.Equal(new[] { visible.Id }, await access.ForUserIdAsync("agent", () => db.Tickets.Select(t => t.Id).ToArrayAsync()));
        account = new TicketAccount("Company", "abc");
        Assert.Empty(await access.ForUserIdAsync("agent", () => db.Tickets.ToListAsync()));
        Assert.Equal(2, await access.ForUserIdAsync("admin", () => db.Tickets.CountAsync()));
        Assert.Equal(2, await db.Tickets.CountAsync());
    }

    [Fact]
    public async Task LoadedModelsAndBulkQueries_CannotMutateHiddenTickets()
    {
        var (host, visible, hidden) = await StartAsync();
        await using var lease = host;
        using var scope = host.Services.CreateScope();
        var access = scope.ServiceProvider.GetRequiredService<TicketAccess>();
        var db = scope.ServiceProvider.GetRequiredService<EscalatedDbContext>();
        var ticket = await db.Tickets.FindAsync(hidden.Id);
        await Assert.ThrowsAsync<TicketAccessDeniedException>(() => access.ForUserIdAsync("agent", async () =>
        {
            ticket!.Subject = "Forbidden";
            await db.SaveChangesAsync();
            return true;
        }));
        db.ChangeTracker.Clear();
        await access.ForUserIdAsync("agent", async () =>
        {
            Assert.Equal(1, await db.Tickets.Where(t => t.Id == visible.Id || t.Id == hidden.Id).ExecuteUpdateAsync(set => set.SetProperty(t => t.Priority, TicketPriority.High)));
            return true;
        });
        var unchanged = await db.Tickets.FindAsync(hidden.Id);
        Assert.Equal("Private search marker", unchanged!.Subject);
        Assert.Equal(TicketPriority.Medium, unchanged.Priority);
    }

    [Fact]
    public async Task RelatedRecords_AreScopedOnTheirOwnQueries()
    {
        var (host, visible, hidden) = await StartAsync();
        await using var lease = host;
        await host.SeedAsync(async db =>
        {
            db.Replies.AddRange(new Reply { TicketId = visible.Id, Body = "Visible" }, new Reply { TicketId = hidden.Id, Body = "Private" });
            db.TicketLinks.Add(new TicketLink { ParentTicketId = visible.Id, ChildTicketId = hidden.Id, LinkType = "related" });
            db.CustomFieldValues.Add(new CustomFieldValue { CustomField = new CustomField { Name = "Access", Slug = "access" }, EntityType = "ticket", EntityId = hidden.Id });
            await db.SaveChangesAsync();
        });
        using var scope = host.Services.CreateScope();
        var access = scope.ServiceProvider.GetRequiredService<TicketAccess>();
        var db = scope.ServiceProvider.GetRequiredService<EscalatedDbContext>();
        await access.ForUserIdAsync("agent", async () =>
        {
            Assert.Single(await db.Replies.ToListAsync());
            Assert.Empty(await db.TicketLinks.ToListAsync());
            Assert.Empty(await db.CustomFieldValues.ToListAsync());
            return true;
        });
    }

    [Fact]
    public async Task TargetedSlas_RankSpecificityAndPreserveTheClockOnMoves()
    {
        var (host, visible, hidden) = await StartAsync(options => options.TargetedSlas = true);
        await using var lease = host;
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EscalatedDbContext>();
        var sla = scope.ServiceProvider.GetRequiredService<SlaService>();
        var baseline = new SlaPolicy { Name = "Default", IsDefault = true, FirstResponseHours = "{\"medium\":8}" };
        var targeted = new SlaPolicy { Name = "Targeted", DepartmentId = visible.DepartmentId, TicketType = "question", FirstResponseHours = "{\"medium\":1}" };
        db.SlaPolicies.AddRange(baseline, targeted);
        await db.SaveChangesAsync();
        var ticket = await db.Tickets.FindAsync(visible.Id);
        await sla.AttachDefaultPolicyAsync(ticket!);
        Assert.Equal(targeted.Id, ticket!.SlaPolicyId);
        var due = ticket.FirstResponseDueAt;
        Assert.Equal(ticket.CreatedAt.AddHours(1), due);
        await scope.ServiceProvider.GetRequiredService<TicketService>().ChangeDepartmentAsync(ticket, hidden.DepartmentId!.Value);
        Assert.Equal(targeted.Id, ticket.SlaPolicyId);
        Assert.Equal(due, ticket.FirstResponseDueAt);
        await sla.AttachDefaultPolicyAsync(ticket);
        Assert.Equal(baseline.Id, ticket.SlaPolicyId);
        Assert.Equal(ticket.CreatedAt.AddHours(8), ticket.FirstResponseDueAt);
        var created = await scope.ServiceProvider.GetRequiredService<TicketService>().CreateAsync("Targeted creation", "Details", departmentId: visible.DepartmentId, ticketType: "question");
        Assert.Equal(targeted.Id, created.SlaPolicyId);
        Assert.Equal(created.CreatedAt.AddHours(1), created.FirstResponseDueAt);
    }

    [Fact]
    public async Task AccountResolver_FillsRequesterAndSubjectAccounts()
    {
        var (host, visible, _) = await StartAsync(options =>
        {
            options.Accounts.Enabled = true;
            options.Accounts.Resolver = (reference, _) => Task.FromResult<TicketAccount?>(new TicketAccount("Company", reference.Kind));
        });
        await using var lease = host;
        using var scope = host.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<TicketService>();
        var ticket = await service.CreateAsync("Resolver", "Details", requesterId: "customer", requesterType: "User");
        Assert.Equal("requester", ticket.AccountId);
        var db = scope.ServiceProvider.GetRequiredService<EscalatedDbContext>();
        var existing = await db.Tickets.FindAsync(visible.Id);
        await scope.ServiceProvider.GetRequiredService<TicketSubjectService>().AttachAsync(existing!, "Project", "42");
        Assert.Equal("subject", existing!.AccountId);
    }


    [Fact]
    public async Task BearerApi_UsesTokenOwnerForListsReportsAndBulkUpdates()
    {
        var (host, visible, hidden) = await StartAsync(services: services => services.AddControllers().AddApplicationPart(typeof(TicketAccessApiController).Assembly));
        await using var lease = host;
        await host.GrantRoleAsync("7", "escalated-agent");
        await host.SeedAsync(async db =>
        {
            db.DepartmentAgents.Add(new DepartmentAgent { DepartmentId = visible.DepartmentId!.Value, AgentId = "7" });
            db.ApiTokens.Add(new ApiToken { TokenableId = 7, TokenableType = "User", Name = "Access test", TokenHash = ApiTokenAuthMiddleware.ComputeSha256("private-token") });
            await db.SaveChangesAsync();
        });
        var client = host.ClientFor("admin");
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "private-token");
        using var response = await client.GetAsync("/ticket-access-api");
        response.EnsureSuccessStatusCode();
        Assert.Equal(new[] { visible.Id }, await response.Content.ReadFromJsonAsync<int[]>());
        using var report = await client.GetAsync("/ticket-access-api/report");
        Assert.Equal(1, await report.Content.ReadFromJsonAsync<int>());
        using var bulk = await client.PostAsync("/ticket-access-api/bulk", null);
        bulk.EnsureSuccessStatusCode();
        Assert.Equal(TicketPriority.Medium, await host.QueryAsync(async db => (await db.Tickets.FindAsync(hidden.Id))!.Priority));
    }

    [Theory]
    [InlineData("automation")]
    [InlineData("escalation")]
    [InlineData("workflow")]
    public async Task RoutingRules_RequireTheCompleteAccountAndTicketType(string engine)
    {
        var (host, visible, hidden) = await StartAsync();
        await using var lease = host;
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EscalatedDbContext>();
        var target = await db.Tickets.SingleAsync(t => t.Id == visible.Id);
        var other = await db.Tickets.SingleAsync(t => t.Id == hidden.Id);
        target.AccountType = other.AccountType = "Company";
        target.AccountId = "ABC";
        other.AccountId = "abc";
        db.Tickets.AddRange(
            new Ticket { Reference = "RULE-TYPE", Subject = "Wrong stream", AccountType = "Company", AccountId = "ABC", TicketType = "incident" },
            new Ticket { Reference = "RULE-ACCOUNT", Subject = "Wrong model", AccountType = "Project", AccountId = "ABC", TicketType = "question" });
        const string conditions = """[{"field":"account","operator":"equals","value":{"type":"Company","id":"ABC"}},{"field":"ticket_type","operator":"equals","value":"question"}]""";
        const string actions = """[{"type":"change_priority","value":"high"}]""";
        if (engine == "automation") db.Automations.Add(new Automation { Name = "Account rule", Conditions = conditions, Actions = actions, Active = true });
        if (engine == "escalation") db.EscalationRules.Add(new EscalationRule { Name = "Account rule", Conditions = conditions, Actions = actions, IsActive = true });
        if (engine == "workflow") db.Workflows.Add(new Workflow { Name = "Account rule", Conditions = "{\"all\":" + conditions + "}", Actions = actions, IsActive = true, TriggerEvent = "ticket.created" });
        await db.SaveChangesAsync();
        if (engine == "automation") Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AutomationRunner>().RunAsync());
        if (engine == "escalation") Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<EscalationService>().EvaluateRulesAsync());
        if (engine == "workflow")
            foreach (var ticket in await db.Tickets.ToListAsync()) await scope.ServiceProvider.GetRequiredService<WorkflowRunnerService>().RunForEventAsync("ticket.created", ticket);
        Assert.Equal(new[] { visible.Id }, await db.Tickets.Where(t => t.Priority == TicketPriority.High).Select(t => t.Id).ToArrayAsync());
        Assert.Empty(await TicketRoutingConditions.WhereAccount(db.Tickets, "{\"id\":\"ABC\"}", "not_equals").ToListAsync());
        Assert.False(new WorkflowEngine().EvaluateSingle(new() { ["field"] = "account", ["operator"] = "not_equals", ["value"] = "{\"id\":\"ABC\"}" }, new() { ["account_type"] = "Company", ["account_id"] = "ABC" }));
    }

    [Fact]
    public async Task LiveUpdates_RecheckMembershipAndGuestTokenAtDelivery()
    {
        var (host, visible, hidden) = await StartAsync();
        await using var lease = host;
        using var scope = host.Services.CreateScope();
        var access = scope.ServiceProvider.GetRequiredService<TicketAccess>();
        var db = scope.ServiceProvider.GetRequiredService<EscalatedDbContext>();
        var audience = scope.ServiceProvider.GetRequiredService<TicketRealtimeAudience>();
        var clients = new Mock<IHubClients>(MockBehavior.Strict);
        var agentClient = new Mock<ISingleClientProxy>();
        var adminClient = new Mock<ISingleClientProxy>();
        clients.Setup(c => c.Client("agent-connection")).Returns(agentClient.Object);
        clients.Setup(c => c.Client("admin-connection")).Returns(adminClient.Object);
        var transport = new Mock<IHubContext<EscalatedHub>>();
        transport.SetupGet(h => h.Clients).Returns(clients.Object);
        var broadcasts = new TicketBroadcastService(access, db, audience, transport.Object);
        var context = new Mock<HubCallerContext>();
        context.SetupGet(c => c.ConnectionId).Returns("agent-connection");
        context.SetupGet(c => c.User).Returns(await access.PrincipalAsync("agent"));
        var groups = new Mock<IGroupManager>();
        var hub = new EscalatedHub(access, db, audience, broadcasts) { Context = context.Object, Groups = groups.Object };
        await hub.JoinTicket(visible.Id);
        await hub.JoinTicketList();
        await Assert.ThrowsAsync<HubException>(() => hub.JoinTicket(hidden.Id));
        groups.Verify(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(new[] { "agent-connection" }, await broadcasts.RecipientsAsync(visible.Id, "ticket-list"));
        Assert.Empty(await broadcasts.RecipientsAsync(hidden.Id, "ticket-list"));
        await broadcasts.SendAsync(visible.Id, "ticket-list", "Updated", new { id = visible.Id });
        audience.Add(new TicketSubscription("admin-connection", "ticket-list", "admin", null, null, true));
        audience.Add(new TicketSubscription("guest-connection", $"ticket-{hidden.Id}", null, "private-token", hidden.Id, false));
        Assert.Equal(new[] { "guest-connection" }, await broadcasts.RecipientsAsync(hidden.Id, $"ticket-{hidden.Id}"));
        await db.DepartmentAgents.ExecuteDeleteAsync();
        await db.Tickets.Where(t => t.Id == hidden.Id).ExecuteUpdateAsync(set => set.SetProperty(t => t.GuestToken, "rotated"));
        Assert.Equal(new[] { "admin-connection" }, await broadcasts.RecipientsAsync(visible.Id, "ticket-list"));
        Assert.Empty(await broadcasts.RecipientsAsync(hidden.Id, $"ticket-{hidden.Id}"));
        await broadcasts.SendAsync(visible.Id, "ticket-list", "Updated", new { id = visible.Id });
        agentClient.Verify(c => c.SendCoreAsync("Updated", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Once);
        adminClient.Verify(c => c.SendCoreAsync("Updated", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Once);
        await hub.OnDisconnectedAsync(null);
        Assert.DoesNotContain(audience.Members, member => member.ConnectionId == "agent-connection");
    }

    [Fact]
    public async Task AssignmentAndFollowerRecipients_ExcludeAgentsWithoutAccess()
    {
        var (host, visible, hidden) = await StartAsync();
        await using var lease = host;
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EscalatedDbContext>();
        var access = scope.ServiceProvider.GetRequiredService<TicketAccess>();
        var assignments = scope.ServiceProvider.GetRequiredService<AssignmentService>();
        var ticket = await db.Tickets.SingleAsync(t => t.Id == hidden.Id);
        await Assert.ThrowsAsync<TicketAccessDeniedException>(() => assignments.AssignAsync(ticket, "agent"));
        Assert.Equal(new[] { "admin" }, await FollowerRecipients.ResolveAsync(new[] { "agent", "admin" }, null, hidden.Id, access));
        Assert.Equal(new[] { "agent", "admin" }, await FollowerRecipients.ResolveAsync(new[] { "agent", "admin" }, null, visible.Id, access));
    }

    [Fact]
    public async Task DefaultsAndUnroutedTickets_PreserveLegacyAccessAndFailClosedWhenEnabled()
    {
        var (host, _, _) = await StartAsync(options => options.Visibility = "all");
        await using var lease = host;
        using var scope = host.Services.CreateScope();
        var access = scope.ServiceProvider.GetRequiredService<TicketAccess>();
        var db = scope.ServiceProvider.GetRequiredService<EscalatedDbContext>();
        db.Tickets.Add(new Ticket { Reference = "UNROUTED", Subject = "Unrouted" });
        await db.SaveChangesAsync();
        Assert.Equal(3, await access.ForUserIdAsync("agent", () => db.Tickets.CountAsync()));
        scope.ServiceProvider.GetRequiredService<IOptions<EscalatedOptions>>().Value.Visibility = "department";
        Assert.Equal(1, await access.ForUserIdAsync("agent", () => db.Tickets.CountAsync()));
        Assert.Equal(3, await access.ForUserIdAsync("admin", () => db.Tickets.CountAsync()));
        Assert.Equal(3, await db.Tickets.CountAsync());
    }

    [Fact]
    public async Task IndirectChildWrites_CannotUsePreviouslyLoadedPrivateRecords()
    {
        var (host, _, hidden) = await StartAsync();
        await using var lease = host;
        using var scope = host.Services.CreateScope();
        var access = scope.ServiceProvider.GetRequiredService<TicketAccess>();
        var db = scope.ServiceProvider.GetRequiredService<EscalatedDbContext>();
        var reply = new Reply { TicketId = hidden.Id, Body = "Private" };
        db.Replies.Add(reply);
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<TicketAccessDeniedException>(() => access.ForUserIdAsync("agent", async () =>
        {
            db.Mentions.Add(new Mention { ReplyId = reply.Id, UserId = "agent" });
            await db.SaveChangesAsync();
            return true;
        }));
        db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<TicketAccessDeniedException>(() => access.ForUserIdAsync("agent", async () =>
        {
            db.Attachments.Add(new Attachment { AttachableType = "reply", AttachableId = reply.Id, Filename = "private.txt" });
            await db.SaveChangesAsync();
            return true;
        }));
    }

    [Fact]
    public async Task ConcurrentRequests_DoNotShareTheirActor()
    {
        var (host, visible, _) = await StartAsync();
        await using var lease = host;
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(async index =>
        {
            using var response = await host.ClientFor(index % 2 == 0 ? "agent" : "admin").GetAsync(index % 2 == 0 ? "/support/agent/tickets" : "/support/admin/tickets");
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return (index, count: json.RootElement.GetProperty("data").GetArrayLength());
        }));
        foreach (var result in results) Assert.Equal(result.index % 2 == 0 ? 1 : 2, result.count);
        Assert.Null(TicketAccessScope.Current);
    }

}


[ApiController]
[Route("ticket-access-api")]
[MiddlewareFilter(typeof(TicketAccessApiPipeline))]
public class TicketAccessApiController : ControllerBase
{
    private readonly EscalatedDbContext db;
    public TicketAccessApiController(EscalatedDbContext db) => this.db = db;
    [HttpGet] public Task<int[]> List() => db.Tickets.Select(t => t.Id).ToArrayAsync();
    [HttpGet("report")] public Task<int> Report() => db.Tickets.CountAsync();
    [HttpPost("bulk")] public Task<int> Bulk() => db.Tickets.ExecuteUpdateAsync(set => set.SetProperty(t => t.Priority, TicketPriority.High));
}

public class TicketAccessApiPipeline
{
    public void Configure(IApplicationBuilder app) => app.UseMiddleware<ApiTokenAuthMiddleware>();
}
