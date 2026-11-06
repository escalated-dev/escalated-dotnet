using System.Net;
using System.Text.Json;
using Escalated.Authorization;
using Escalated.Plugins;
using Escalated.Tests.Hosting;
using Escalated.Tests.Plugins;
using Escalated.Ui;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Escalated.Tests.Embed.EmbedTestSupport;
using static Escalated.Tests.Hosting.InertiaTestSupport;

namespace Escalated.Tests.Embed;

/// <summary>
/// <c>escalated.navigation</c> and <c>EscalatedAdminNavigation</c> (ADR
/// 2026-11-05-embeddable-admin): the documented item shape, order and active item; filtering by
/// the admin gate, routes served, feature switches and route permissions; plugin admin items.
/// </summary>
public class AdminNavigationTests
{
    private static async Task<List<JsonElement>> NavigationAsync(EscalatedTestHost host, string user, string url = "/backoffice/helpdesk/tags")
    {
        var page = await PageAsync(host.ClientFor(user, HostClaim), url);
        return SharedProps(page).GetProperty("navigation").EnumerateArray().ToList();
    }

    private static List<string> Keys(IEnumerable<JsonElement> items) => items.Select(i => i.GetProperty("key").GetString()!).ToList();

    [Fact]
    public async Task SharesTheMenuWithTheDocumentedShapeInOrderWithTheActiveItem()
    {
        await using var host = await StartAsync();

        var items = await NavigationAsync(host, Admin);

        Assert.Equal(
            """{"key":"reports","label":"Reports","route":"escalated.admin.reports","url":"/backoffice/helpdesk/reports","icon":"reports","position":10,"active":false,"badge":null,"plugin":null,"children":[]}""",
            items[0].GetRawText());
        Assert.Equal("tickets", items[1].GetProperty("key").GetString());
        Assert.Equal("settings", items[^1].GetProperty("key").GetString());
        var positions = items.Select(i => i.GetProperty("position").GetDouble()).ToList();
        Assert.Equal(positions.OrderBy(p => p).ToList(), positions);
        Assert.Equal(new[] { "tags" }, Keys(items.Where(i => i.GetProperty("active").GetBoolean())));
        Assert.Equal(53.5, items.Single(i => i.GetProperty("key").GetString() == "automations").GetProperty("position").GetDouble());
        Assert.Equal(
            new[]
            {
                "reports", "tickets", "departments", "sla-policies", "knowledge-base", "escalation-rules", "workflows", "automations",
                "webhooks", "tags", "custom-fields", "statuses", "canned-responses", "macros", "skills", "users", "capacity",
                "business-hours", "audit-log", "api-tokens", "custom-objects", "plugins", "import", "settings",
            },
            Keys(items));
    }

    [Fact]
    public async Task LeavesOutScreensWhoseFeatureIsOff()
    {
        await using (var off = await StartAsync())
        {
            var keys = Keys(await NavigationAsync(off, Admin));
            Assert.DoesNotContain("roles", keys);
            Assert.DoesNotContain("newsletters", keys);
        }

        await using var on = await StartAsync(options: o =>
        {
            o.EnableNewsletters = true;
            o.Access.RolesPermissions = true;
        });
        using (var scope = on.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<PermissionSeeder>().SeedAsync();
        }

        await on.GrantRoleAsync(Admin, "admin");

        var items = await NavigationAsync(on, Admin);
        Assert.Contains("roles", Keys(items));
        var newsletters = items.Single(i => i.GetProperty("key").GetString() == "newsletters");
        Assert.Equal("/admin/newsletters", newsletters.GetProperty("url").GetString());
        Assert.Equal(81, newsletters.GetProperty("position").GetDouble());
    }

    [Fact]
    public async Task FiltersTheMenuByRoutePermissionWhileRolesAndPermissionsIsOn()
    {
        await using var host = await StartAsync(options: o => o.Access.RolesPermissions = true);
        using (var scope = host.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<PermissionSeeder>().SeedAsync();
        }

        await host.GrantRoleAsync(Admin, await EmbeddedAdminLayoutTests.RoleAsync(host, "tag.view", "ticket.view"));

        var keys = Keys(await NavigationAsync(host, Admin));

        Assert.Contains("tags", keys);
        Assert.Contains("tickets", keys);
        Assert.DoesNotContain("settings", keys);
        Assert.DoesNotContain("users", keys);
        Assert.DoesNotContain("roles", keys);
    }

    [Fact]
    public async Task GivesNoMenuToAUserWhoMayNotUseTheAdmin()
    {
        await using var host = await StartAsync();

        var page = await PageAsync(host.ClientFor(Agent, HostClaim), "/support/agent");

        Assert.Equal(0, SharedProps(page).GetProperty("navigation").GetArrayLength());
    }

    [Fact]
    public async Task IncludesPluginAdminMenuItems()
    {
        await using var host = await StartAsync(services: s =>
        {
            s.AddSingleton(new PluginRecorder());
            s.AddEscalatedPlugin<CrmPlugin>();
        });
        var admin = host.ClientFor(Admin, HostClaim);
        var activated = await admin.SendAsync(Visit(HttpMethod.Post, $"/backoffice/helpdesk/plugins/{CrmPlugin.Slug}/activate"));
        Assert.Equal(HttpStatusCode.SeeOther, activated.StatusCode);
        Assert.Equal("/backoffice/helpdesk/plugins", activated.Headers.Location!.OriginalString);
        await host.Services.GetRequiredService<Escalated.Plugins.PluginEventRelay>().WhenIdleAsync();

        var items = await NavigationAsync(host, Admin, $"/backoffice/helpdesk/plugins/{CrmPlugin.Slug}/dashboard");
        var crm = items.Single(i => i.GetProperty("label").GetString() == "CRM");

        Assert.Equal($"plugin:{CrmPlugin.Slug}:0", crm.GetProperty("key").GetString());
        Assert.Equal(JsonValueKind.Null, crm.GetProperty("route").ValueKind);
        Assert.Equal($"/backoffice/helpdesk/plugins/{CrmPlugin.Slug}/dashboard", crm.GetProperty("url").GetString());
        Assert.Equal("users", crm.GetProperty("icon").GetString());
        Assert.Equal(5, crm.GetProperty("position").GetDouble());
        Assert.Equal(CrmPlugin.Slug, crm.GetProperty("plugin").GetString());
        Assert.True(crm.GetProperty("active").GetBoolean());
        // Sorted by position with the core items: before Reports (10).
        Assert.Equal(0, items.IndexOf(crm));
        Assert.DoesNotContain(items, i => i.GetProperty("label").GetString() == "Secret");

        var plugin = await admin.SendAsync(Visit(HttpMethod.Get, $"/backoffice/helpdesk/plugins/{CrmPlugin.Slug}/dashboard"));
        Assert.Equal(HttpStatusCode.OK, plugin.StatusCode);
    }

    [Fact]
    public async Task TheHelperWorksWithoutEmbeddingAndFollowsTheStandaloneAdmin()
    {
        await using var host = await EscalatedTestHost.StartAsync(o => o.Ui.Enabled = true);
        await host.GrantRoleAsync(Admin, "escalated-admin");

        using var scope = host.Services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Path = "/support/admin/tags";
        context.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, Admin) }, "Test"));

        var items = await context.EscalatedAdminNavigationAsync();

        Assert.Equal("/support/admin/reports", items[0].Url);
        Assert.Equal("tags", items.Single(i => i.Active).Key);

        var anonymous = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        Assert.Empty(await anonymous.EscalatedAdminNavigationAsync());
    }
}
