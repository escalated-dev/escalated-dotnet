using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Escalated.Data;
using Escalated.Enums;
using Escalated.Models;
using Escalated.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Escalated.Tests.Hosting;

/// <summary>
/// Who may reach which surface, and whose identity an action acts as.
///
/// <para>Nothing in the package was authorized. Every admin, agent and customer
/// endpoint answered an anonymous request, and the endpoints that needed to know
/// the caller took the caller's id from the request itself: a
/// <c>requesterId</c>, <c>agentId</c>, <c>userId</c> or <c>currentUserId</c>
/// query parameter, or a <c>RequesterId</c>/<c>AuthorId</c>/<c>CauserId</c> in the
/// body. Changing it was enough to read another customer's ticket, reply as them,
/// or read another agent's mentions.</para>
///
/// <para>The admin and agent surfaces mirror the Laravel reference's
/// <c>escalated-admin</c> and <c>escalated-agent</c> gates, and by default grant
/// access through the roles of the same name that the admin users page manages.</para>
/// </summary>
public class AuthorizationTests
{
    private const string AdminRole = "escalated-admin";
    private const string AgentRole = "escalated-agent";

    [Theory]
    [InlineData("/support/admin/tickets")]
    [InlineData("/support/admin/users")]
    [InlineData("/support/admin/reports")]
    [InlineData("/support/admin/webhooks")]
    [InlineData("/support/agent/tickets")]
    [InlineData("/support/agent/mentions")]
    [InlineData("/support/agent/chat/queue")]
    [InlineData("/support/tickets")]
    public async Task AnonymousRequest_IsUnauthorized(string path)
    {
        await using var host = await EscalatedTestHost.StartAsync();

        var response = await host.Client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/support/admin/tickets")]
    [InlineData("/support/admin/api-tokens")]
    [InlineData("/support/agent/tickets")]
    public async Task UserWithoutARole_IsForbiddenFromStaffSurfaces(string path)
    {
        await using var host = await EscalatedTestHost.StartAsync();

        var response = await host.ClientFor("customer-5").GetAsync(path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Agent_ReachesTheAgentSurface_ButNotAdmin()
    {
        await using var host = await EscalatedTestHost.StartAsync();
        await host.GrantRoleAsync("agent-2", AgentRole);
        var agent = host.ClientFor("agent-2");

        Assert.Equal(HttpStatusCode.OK, (await agent.GetAsync("/support/agent/tickets")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await agent.GetAsync("/support/admin/tickets")).StatusCode);
    }

    [Fact]
    public async Task Admin_ReachesAdminAndAgentSurfaces()
    {
        await using var host = await EscalatedTestHost.StartAsync();
        await host.GrantRoleAsync("admin-1", AdminRole);
        var admin = host.ClientFor("admin-1");

        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/support/admin/tickets")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/support/agent/tickets")).StatusCode);
    }

    [Fact]
    public async Task Customer_CannotReadAnotherCustomersTicket_ByChangingTheQuery()
    {
        await using var host = await EscalatedTestHost.StartAsync();
        var aliceTicket = await SeedTicketAsync(host, "alice");

        var response = await host.ClientFor("mallory").GetAsync($"/support/tickets/{aliceTicket}?requesterId=alice");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Customer_ReadsTheirOwnTicket()
    {
        await using var host = await EscalatedTestHost.StartAsync();
        var aliceTicket = await SeedTicketAsync(host, "alice");

        var response = await host.ClientFor("alice").GetAsync($"/support/tickets/{aliceTicket}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Customer_TicketList_IsTheirOwn_WhateverTheQuerySays()
    {
        await using var host = await EscalatedTestHost.StartAsync();
        await SeedTicketAsync(host, "alice");
        var malloryTicket = await SeedTicketAsync(host, "mallory");

        var response = await host.ClientFor("mallory").GetAsync("/support/tickets?requesterId=alice");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ids = (await DataAsync(response)).Select(t => t.GetProperty("id").GetInt32()).ToList();
        Assert.Equal(new[] { malloryTicket }, ids);
    }

    [Fact]
    public async Task Customer_CannotReplyAsAnotherRequester()
    {
        await using var host = await EscalatedTestHost.StartAsync();
        var aliceTicket = await SeedTicketAsync(host, "alice");

        var response = await host.ClientFor("mallory")
            .PostAsJsonAsync($"/support/tickets/{aliceTicket}/reply", new { body = "hi", requesterId = "alice" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, await host.QueryAsync(db => db.Replies.CountAsync()));
    }

    [Fact]
    public async Task Customer_CreatesTicketsAsThemselves()
    {
        await using var host = await EscalatedTestHost.StartAsync();

        var response = await host.ClientFor("mallory")
            .PostAsJsonAsync("/support/tickets", new { subject = "Refund", requesterId = "alice" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var requester = await host.QueryAsync(db => db.Tickets.Select(t => t.RequesterId).SingleAsync());
        Assert.Equal("mallory", requester);
    }

    [Fact]
    public async Task Agent_MentionInbox_IsTheirOwn_WhateverTheQuerySays()
    {
        await using var host = await EscalatedTestHost.StartAsync();
        await host.GrantRoleAsync("agent-8", AgentRole);
        await host.SeedAsync(async db =>
        {
            var ticket = NewTicket("alice");
            db.Tickets.Add(ticket);
            await db.SaveChangesAsync();
            var reply = new Reply { TicketId = ticket.Id, Body = "cc", AuthorId = "1", IsInternalNote = true, Type = "note" };
            db.Replies.Add(reply);
            await db.SaveChangesAsync();
            db.Mentions.Add(new Mention { ReplyId = reply.Id, UserId = "agent-7" });
            db.Mentions.Add(new Mention { ReplyId = reply.Id, UserId = "agent-8" });
            await db.SaveChangesAsync();
        });

        var response = await host.ClientFor("agent-8").GetAsync("/support/agent/mentions?userId=agent-7");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var mine = await host.QueryAsync(db => db.Mentions.Where(m => m.UserId == "agent-8").Select(m => m.Id).SingleAsync());
        var ids = (await DataAsync(response)).Select(m => m.GetProperty("id").GetInt32()).ToList();
        Assert.Equal(new[] { mine }, ids);
    }

    [Fact]
    public async Task Admin_CannotRemoveTheirOwnAdminRole_WithoutSayingWhoTheyAre()
    {
        await using var host = await EscalatedTestHost.StartAsync(
            configureServices: s => s.AddSingleton<IUserDirectory, EveryoneDirectory>());
        await host.GrantRoleAsync("admin-42", AdminRole);

        var response = await host.ClientFor("admin-42")
            .PatchAsJsonAsync("/support/admin/users/admin-42/role", new { role = "admin", value = false });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AgentAction_IsAttributedToTheAuthenticatedAgent()
    {
        await using var host = await EscalatedTestHost.StartAsync();
        await host.GrantRoleAsync("agent-2", AgentRole);
        var ticket = await SeedTicketAsync(host, "alice");

        var response = await host.ClientFor("agent-2").PostAsJsonAsync(
            "/support/agent/tickets/bulk",
            new { ticketIds = new[] { ticket }, action = "close", causerId = "agent-9" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var causers = await host.QueryAsync(db =>
            db.TicketActivities.Where(a => a.TicketId == ticket).Select(a => a.CauserId).Distinct().ToListAsync());
        Assert.Equal(new[] { "agent-2" }, causers);
    }

    [Fact]
    public async Task Customer_CannotDownloadAnotherCustomersAttachment()
    {
        await using var host = await EscalatedTestHost.StartAsync();
        var aliceTicket = await SeedTicketAsync(host, "alice");
        var attachment = await SeedAttachmentAsync(host, aliceTicket);

        var mallory = await host.ClientFor("mallory").GetAsync($"/support/attachments/{attachment}/download");
        var alice = await host.ClientFor("alice").GetAsync($"/support/attachments/{attachment}/download");
        var anonymous = await host.Client.GetAsync($"/support/attachments/{attachment}/download");

        Assert.Equal(HttpStatusCode.Forbidden, mallory.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, alice.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task EscalatedAdmin_ManagesNewsletters()
    {
        await using var host = await EscalatedTestHost.StartAsync(o => o.EnableNewsletters = true);
        await host.GrantRoleAsync("admin-1", AdminRole);

        var response = await host.ClientFor("admin-1").GetAsync("/admin/newsletters");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// With the admin policy replaced, a user the host lets into the admin panel still
    /// needs a newsletter permission. Denied, that is a 403, not an unhandled exception.
    /// </summary>
    [Fact]
    public async Task HostAdminWithoutNewsletterPermission_IsForbidden_NotAServerError()
    {
        await using var host = await StartWithHostAdminPolicyAsync();

        var response = await host.ClientFor("lead-1", (SupportLeadClaim, "true")).GetAsync("/admin/newsletters");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task HostAdminWithTheNewsletterPermission_ManagesNewsletters()
    {
        await using var host = await StartWithHostAdminPolicyAsync();
        await GrantRoleWithPermissionAsync(host, "lead-1", "newsletter-editor", "newsletters.manage");

        var response = await host.ClientFor("lead-1", (SupportLeadClaim, "true")).GetAsync("/admin/newsletters");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// The admin role is <c>escalated-admin</c>. A role that merely has the slug
    /// <c>admin</c> (creating a role named "Admin" on the settings page makes one)
    /// holds the permissions attached to it and nothing more.
    /// </summary>
    [Fact]
    public async Task RoleWithTheSlugAdmin_HoldsNoNewsletterPermissionItWasNotGiven()
    {
        await using var host = await StartWithHostAdminPolicyAsync();
        await host.GrantRoleAsync("lead-1", "admin");

        var response = await host.ClientFor("lead-1", (SupportLeadClaim, "true")).GetAsync("/admin/newsletters");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private const string SupportLeadClaim = "support-lead";

    /// <summary>Newsletters on, and the admin policy replaced the way the README shows.</summary>
    private static Task<EscalatedTestHost> StartWithHostAdminPolicyAsync() =>
        EscalatedTestHost.StartAsync(
            o => o.EnableNewsletters = true,
            services => services.AddAuthorization(options =>
                options.AddPolicy(Escalated.Authorization.EscalatedPolicies.Admin, p => p.RequireClaim(SupportLeadClaim))));

    private static Task GrantRoleWithPermissionAsync(EscalatedTestHost host, string userId, string roleSlug, string permissionSlug) =>
        host.SeedAsync(async db =>
        {
            var role = new Role { Name = roleSlug, Slug = roleSlug };
            role.Permissions.Add(new Permission { Name = permissionSlug, Slug = permissionSlug });
            role.Users.Add(new RoleUser { UserId = userId });
            db.Roles.Add(role);
            await db.SaveChangesAsync();
        });

    [Fact]
    public async Task PublicSurfaces_StayPublic()
    {
        await using var host = await EscalatedTestHost.StartAsync(o => o.EnableNewsletters = true);

        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/support/widget/kb/search?q=printer")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/escalated/n/o/abc123.gif")).StatusCode);
    }

    [Fact]
    public void EveryControllerDeclaresWhoMayReachIt()
    {
        var undeclared = typeof(EscalatedDbContext).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true } && t.Name.EndsWith("Controller"))
            .Where(t => !t.IsDefined(typeof(AuthorizeAttribute), inherit: true)
                        && !t.IsDefined(typeof(AllowAnonymousAttribute), inherit: true))
            .Select(t => t.Name)
            .OrderBy(n => n)
            .ToList();

        Assert.True(undeclared.Count == 0,
            "Controllers with neither [Authorize] nor [AllowAnonymous]:\n" + string.Join("\n", undeclared));
    }

    private static Ticket NewTicket(string requesterId) => new()
    {
        Subject = $"Ticket for {requesterId}",
        Reference = $"ESC-{Guid.NewGuid():N}"[..20],
        RequesterId = requesterId,
        Status = TicketStatus.Open,
        Priority = TicketPriority.Medium,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    private static async Task<int> SeedTicketAsync(EscalatedTestHost host, string requesterId)
    {
        var id = 0;
        await host.SeedAsync(async db =>
        {
            var ticket = NewTicket(requesterId);
            db.Tickets.Add(ticket);
            await db.SaveChangesAsync();
            id = ticket.Id;
        });

        return id;
    }

    private static async Task<int> SeedAttachmentAsync(EscalatedTestHost host, int ticketId)
    {
        var id = 0;
        await host.SeedAsync(async db =>
        {
            var attachment = new Attachment
            {
                AttachableType = "ticket",
                AttachableId = ticketId,
                Filename = "invoice.pdf",
                MimeType = "application/pdf",
                Disk = "s3",
                Path = "https://files.example.com/invoice.pdf",
            };
            db.Attachments.Add(attachment);
            await db.SaveChangesAsync();
            id = attachment.Id;
        });

        return id;
    }

    private static async Task<List<JsonElement>> DataAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("data").EnumerateArray().Select(e => e.Clone()).ToList();
    }

    private sealed class EveryoneDirectory : IUserDirectory
    {
        public Task<UserDirectoryEntry?> FindAsync(string id, CancellationToken ct = default) =>
            Task.FromResult<UserDirectoryEntry?>(new UserDirectoryEntry(id, $"User {id}", $"{id}@example.com"));

        public Task<UserDirectoryPage> ListAsync(string? search, int page, int perPage, CancellationToken ct = default) =>
            Task.FromResult(new UserDirectoryPage(Array.Empty<UserDirectoryEntry>(), 0, page, perPage));
    }
}
