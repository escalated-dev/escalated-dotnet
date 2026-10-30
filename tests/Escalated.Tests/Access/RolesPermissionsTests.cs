using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Escalated.Authorization;
using Escalated.Middleware;
using Escalated.Enums;
using Escalated.Models;
using Escalated.Tests.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Escalated.Tests.Hosting.InertiaTestSupport;

namespace Escalated.Tests.Access;

/// <summary>
/// Roles and permissions (ADR 2026-10-29-configurable-access-features, port test checklist),
/// through the real pipeline: switched on, and off (the default).
/// </summary>
public class RolesPermissionsTests
{
    private static async Task<EscalatedTestHost> StartAsync(bool on, bool tokens = false)
    {
        var host = await EscalatedTestHost.StartAsync(
            options =>
            {
                options.Ui.Enabled = true;
                options.EnableNewsletters = true;
                options.Access.RolesPermissions = on;
            },
            null,
            tokens ? app => app.UseWhen(c => c.Request.Headers.Authorization.Count > 0, b => b.UseMiddleware<ApiTokenAuthMiddleware>()) : null);
        using var scope = host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<PermissionSeeder>().SeedAsync();
        return host;
    }

    /// <summary>A staff user: the gate role, plus <paramref name="roles"/>.</summary>
    private static async Task<HttpClient> StaffAsync(EscalatedTestHost host, string userId, bool admin, params string[] roles)
    {
        await host.GrantRoleAsync(userId, admin ? "escalated-admin" : "escalated-agent");
        foreach (var role in roles)
        {
            await host.GrantRoleAsync(userId, role);
        }

        return host.ClientFor(userId);
    }

    /// <summary>A role holding exactly <paramref name="slugs"/>.</summary>
    private static async Task<string> RoleAsync(EscalatedTestHost host, params string[] slugs)
    {
        var slug = "custom-" + Guid.NewGuid().ToString("N")[..8];
        await host.SeedAsync(async db =>
        {
            var role = new Role { Name = slug, Slug = slug };
            db.Roles.Add(role);
            await db.SaveChangesAsync();
            foreach (var permission in await db.Permissions.Where(p => slugs.Contains(p.Slug)).ToListAsync())
            {
                db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id });
            }

            await db.SaveChangesAsync();
        });
        return slug;
    }

    private static async Task<int> TicketAsync(EscalatedTestHost host)
    {
        var id = 0;
        await host.SeedAsync(async db =>
        {
            var ticket = new Ticket { Reference = "ESC-" + Random.Shared.Next(10000, 99999), Subject = "Printer", Priority = TicketPriority.High };
            db.Tickets.Add(ticket);
            await db.SaveChangesAsync();
            id = ticket.Id;
        });
        return id;
    }

    private static async Task<HttpStatusCode> PostAsync(HttpClient client, string url, object body)
    {
        using var response = await client.SendAsync(Visit(HttpMethod.Post, url, body));
        return response.StatusCode;
    }

    private static async Task<JsonElement> EscalatedAsync(HttpClient client, string url) =>
        (await PageAsync(client, url)).GetProperty("props").GetProperty("escalated");

    [Fact]
    public async Task On_TheSwitchIsSharedAndTheRolesRoutesWork()
    {
        await using var host = await StartAsync(on: true);
        var admin = await StaffAsync(host, "1", admin: true, "admin");
        var escalated = await EscalatedAsync(admin, "/support/admin/roles");
        Assert.True(escalated.GetProperty("features").GetProperty("roles_permissions").GetBoolean());
        Assert.False(escalated.GetProperty("features").GetProperty("two_factor").GetBoolean());
        Assert.False(escalated.GetProperty("features").GetProperty("sso").GetBoolean());
        var slugs = escalated.GetProperty("permissions").EnumerateArray().Select(s => s.GetString()).ToList();
        Assert.Contains("role.manage", slugs);
        Assert.DoesNotContain("*", slugs);

        var light = await StaffAsync(host, "2", admin: false, "light_agent");
        var lightSlugs = (await EscalatedAsync(light, "/support/agent")).GetProperty("permissions").EnumerateArray()
            .Select(s => s.GetString()).OrderBy(s => s).ToList();
        Assert.Equal(new[] { "kb.view", "macro.view", "reply.create", "reply.create_internal", "tag.view", "ticket.view" }, lightSlugs);
    }

    [Fact]
    public async Task On_PassingTheAdminGateWithoutARoleGrantsNothing_AndARoleGrantingThePermissionDoes()
    {
        await using var host = await StartAsync(on: true);
        var ticket = await TicketAsync(host);
        var bare = await StaffAsync(host, "1", admin: true);
        Assert.Equal(HttpStatusCode.Forbidden, (await bare.SendAsync(Visit(HttpMethod.Get, "/support/admin/settings"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, await PostAsync(bare, "/support/admin/departments", new { name = "Ops" }));
        Assert.Equal(HttpStatusCode.Forbidden, await PostAsync(bare, $"/support/agent/tickets/{ticket}/priority", new { priority = "low" }));

        var admin = await StaffAsync(host, "2", admin: true, "admin");
        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(Visit(HttpMethod.Get, "/support/admin/settings"))).StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, await PostAsync(admin, "/support/admin/departments", new { name = "Ops" }));
        Assert.NotEqual(HttpStatusCode.Forbidden, await PostAsync(admin, $"/support/agent/tickets/{ticket}/priority", new { priority = "low" }));

        var reader = await StaffAsync(host, "3", admin: true, await RoleAsync(host, "department.view"));
        Assert.Equal(HttpStatusCode.OK, (await reader.SendAsync(Visit(HttpMethod.Get, "/support/admin/departments"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, await PostAsync(reader, "/support/admin/departments", new { name = "No" }));
    }

    [Fact]
    public async Task On_ALightAgentViewsAndNotesButCannotAssignReprioritiseCloseOrDelete()
    {
        await using var host = await StartAsync(on: true);
        var ticket = await TicketAsync(host);
        var light = await StaffAsync(host, "1", admin: true, "light_agent");

        Assert.Equal(HttpStatusCode.OK, (await light.GetAsync($"/support/agent/tickets/{ticket}")).StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, await PostAsync(light, $"/support/agent/tickets/{ticket}/note", new { body = "Looking" }));
        Assert.Equal(HttpStatusCode.Forbidden, await PostAsync(light, $"/support/agent/tickets/{ticket}/assign", new { agent_id = "1" }));
        Assert.Equal(HttpStatusCode.Forbidden, await PostAsync(light, $"/support/agent/tickets/{ticket}/priority", new { priority = "low" }));
        Assert.Equal(HttpStatusCode.Forbidden, await PostAsync(light, $"/support/agent/tickets/{ticket}/status", new { status = "closed" }));
        Assert.Equal(HttpStatusCode.Forbidden, await PostAsync(light, "/support/admin/tickets/bulk",
            new { ticket_ids = new[] { ticket }, action = "delete" }));
        var row = await host.QueryAsync(db => db.Tickets.AsNoTracking().SingleAsync(t => t.Id == ticket));
        Assert.Equal(TicketPriority.High, row.Priority);
        Assert.NotEqual(TicketStatus.Closed, row.Status);
    }

    [Fact]
    public async Task On_AClosingStatusChangeNeedsTicketClose()
    {
        await using var host = await StartAsync(on: true);
        var ticket = await TicketAsync(host);
        var editor = await StaffAsync(host, "1", admin: false, await RoleAsync(host, "ticket.view", "ticket.edit"));
        Assert.NotEqual(HttpStatusCode.Forbidden, await PostAsync(editor, $"/support/agent/tickets/{ticket}/status", new { status = "in_progress" }));
        foreach (var closing in new[] { "closed", "resolved", "reopened" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, await PostAsync(editor, $"/support/agent/tickets/{ticket}/status", new { status = closing }));
        }

        var closer = await StaffAsync(host, "2", admin: false, await RoleAsync(host, "ticket.view", "ticket.edit", "ticket.close"));
        Assert.NotEqual(HttpStatusCode.Forbidden, await PostAsync(closer, $"/support/agent/tickets/{ticket}/status", new { status = "resolved" }));
    }

    [Fact]
    public async Task On_BulkActionsCheckEachActionsPermission()
    {
        await using var host = await StartAsync(on: true);
        var ticket = await TicketAsync(host);
        var editor = await StaffAsync(host, "1", admin: true, await RoleAsync(host, "ticket.view", "ticket.edit"));
        var ids = new[] { ticket };
        Assert.Equal(HttpStatusCode.SeeOther, await PostAsync(editor, "/support/admin/tickets/bulk", new { ticket_ids = ids, action = "priority", value = "low" }));
        Assert.Equal(HttpStatusCode.Forbidden, await PostAsync(editor, "/support/admin/tickets/bulk", new { ticket_ids = ids, action = "assign", value = "1" }));
        Assert.Equal(HttpStatusCode.Forbidden, await PostAsync(editor, "/support/admin/tickets/bulk", new { ticket_ids = ids, action = "delete" }));
        Assert.Equal(HttpStatusCode.Forbidden, await PostAsync(editor, "/support/admin/tickets/bulk", new { ticket_ids = ids, action = "status", value = "closed" }));
        Assert.Equal(HttpStatusCode.SeeOther, await PostAsync(editor, "/support/admin/tickets/bulk", new { ticket_ids = ids, action = "status", value = "in_progress" }));
    }

    [Fact]
    public async Task On_TheStaffApiChecksTheTokenOwnersPermissions()
    {
        await using var host = await StartAsync(on: true, tokens: true);
        await host.GrantRoleAsync("7", "escalated-agent");
        await host.SeedAsync(async db =>
        {
            db.ApiTokens.Add(new ApiToken { TokenableId = 7, TokenableType = "User", Name = "cli", TokenHash = ApiTokenAuthMiddleware.ComputeSha256("roles-token") });
            await db.SaveChangesAsync();
        });
        var client = host.Client;
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "roles-token");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/support/agent/tickets/dashboard")).StatusCode);

        await host.GrantRoleAsync("7", "light_agent");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/support/agent/tickets/dashboard")).StatusCode);
    }

    [Fact]
    public async Task On_NewslettersNeedTheirPermissions()
    {
        await using var host = await StartAsync(on: true);
        var none = await StaffAsync(host, "1", admin: true, await RoleAsync(host, "ticket.view"));
        Assert.Equal(HttpStatusCode.Forbidden, (await none.GetAsync("/admin/newsletters")).StatusCode);
        var manager = await StaffAsync(host, "2", admin: true, await RoleAsync(host, "newsletters.manage"));
        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync("/admin/newsletters")).StatusCode);
    }

    [Fact]
    public async Task EveryStaffRouteIsInTheMap()
    {
        await using var host = await StartAsync(on: true);
        var unmapped = new List<string>();
        var checkedRoutes = 0;
        foreach (var endpoint in host.Endpoints)
        {
            var template = RoutePermissions.Template(endpoint.RoutePattern.RawText, "support");
            if (template is null || !RoutePermissions.IsStaffRoute(template))
            {
                continue;
            }

            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? new[] { "GET", "POST", "PUT", "PATCH", "DELETE" };
            foreach (var method in methods.Where(m => m != "OPTIONS" && m != "HEAD"))
            {
                checkedRoutes++;
                if (RoutePermissions.Mapped(method, template) is null)
                {
                    unmapped.Add(method + " " + template);
                }
            }
        }

        Assert.True(checkedRoutes > 200, $"only {checkedRoutes} staff routes found");
        Assert.Empty(unmapped);
    }

    [Fact]
    public async Task Off_TheRolesRoutesAnswer404AndTheGatesAloneDecide()
    {
        await using var host = await StartAsync(on: false);
        var ticket = await TicketAsync(host);
        var admin = await StaffAsync(host, "1", admin: true);
        foreach (var path in new[] { "/support/admin/roles", "/support/admin/roles/create", "/support/admin/roles/1/edit" })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await admin.SendAsync(Visit(HttpMethod.Get, path))).StatusCode);
        }

        Assert.Equal(HttpStatusCode.NotFound, await PostAsync(admin, "/support/admin/roles", new { name = "x" }));

        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(Visit(HttpMethod.Get, "/support/admin/settings"))).StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, await PostAsync(admin, "/support/admin/departments", new { name = "Ops" }));
        Assert.NotEqual(HttpStatusCode.Forbidden, await PostAsync(admin, $"/support/agent/tickets/{ticket}/status", new { status = "closed" }));

        var escalated = await EscalatedAsync(admin, "/support/admin/settings");
        Assert.False(escalated.GetProperty("features").GetProperty("roles_permissions").GetBoolean());
        Assert.Equal("[\"*\"]", escalated.GetProperty("permissions").GetRawText());
        var agent = await StaffAsync(host, "2", admin: false);
        Assert.Equal("[\"*\"]", (await EscalatedAsync(agent, "/support/agent")).GetProperty("permissions").GetRawText());
        Assert.Equal(HttpStatusCode.Forbidden, (await agent.SendAsync(Visit(HttpMethod.Get, "/support/admin/settings"))).StatusCode);
    }

    [Fact]
    public async Task Off_ASettingWithTheSwitchsNameCannotTurnItOn()
    {
        await using var host = await StartAsync(on: false);
        var admin = await StaffAsync(host, "1", admin: true);
        await host.SeedAsync(async db =>
        {
            db.Settings.Add(new EscalatedSettings { Key = "roles_permissions", Value = "1" });
            await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.NotFound, (await admin.SendAsync(Visit(HttpMethod.Get, "/support/admin/roles"))).StatusCode);
    }
}
