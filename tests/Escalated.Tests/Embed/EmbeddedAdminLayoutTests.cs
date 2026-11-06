using System.Net;
using System.Text.Json;
using Escalated.Authorization;
using Escalated.Configuration;
using Escalated.Models;
using Escalated.Tests.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Escalated.Tests.Embed.EmbedTestSupport;
using static Escalated.Tests.Hosting.InertiaTestSupport;

namespace Escalated.Tests.Embed;

/// <summary>
/// The admin embedded in a host admin panel, layout mode (ADR 2026-11-05-embeddable-admin):
/// routes under the host's prefix with the host's policy and filter and the same handlers,
/// Escalated's admin gate, two-factor and route permissions kept, the embed flag shared.
/// </summary>
public class EmbeddedAdminLayoutTests
{
    [Fact]
    public async Task MountsTheAdminUnderTheHostPrefixWithTheHostPolicyAndFilter()
    {
        await using var host = await StartAsync();

        var response = await host.ClientFor(Admin, HostClaim).SendAsync(Visit(HttpMethod.Get, "/backoffice/helpdesk/tags"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("1", response.Headers.GetValues("X-Host-Admin").Single());
        var page = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Escalated/Admin/Tags/Index", page.GetProperty("component").GetString());
        Assert.Equal("/backoffice/helpdesk/tags", page.GetProperty("url").GetString());
    }

    [Fact]
    public async Task TheHostPolicyRefusesUsersItDoesNotAdmit()
    {
        await using var host = await StartAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await host.ClientFor(Admin).GetAsync("/backoffice/helpdesk/tags")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync("/backoffice/helpdesk/tags")).StatusCode);
    }

    [Fact]
    public async Task KeepsEscalatedsAdminGate()
    {
        await using var host = await StartAsync();

        var response = await host.ClientFor(Agent, HostClaim).GetAsync("/backoffice/helpdesk/tags");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task NoLongerServesTheStandaloneAdminUrls()
    {
        await using var host = await StartAsync();
        var admin = host.ClientFor(Admin, HostClaim);

        Assert.Equal(HttpStatusCode.NotFound, (await admin.SendAsync(Visit(HttpMethod.Get, "/support/admin/tags"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/support/admin/tickets")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.SendAsync(Visit(HttpMethod.Get, "/support/admin/plugins"))).StatusCode);
    }

    /// <summary>
    /// Every endpoint of the default table (the snapshot) is still there; those that lived under
    /// <c>support/admin</c>, except two-factor enrolment, now live under the host prefix, with the
    /// same methods and handler; nothing else moved.
    /// </summary>
    [Fact]
    public async Task MovesEveryAdminRouteAndNothingElse()
    {
        await using var host = await StartAsync();

        var snapshot = JsonDocument.Parse(await File.ReadAllTextAsync(DefaultRouteTableTests.FixturePath())).RootElement;
        var expected = snapshot.GetProperty("endpoints").EnumerateArray().Select(e =>
        {
            var pattern = e.GetProperty("pattern").GetString()!;
            var moved = pattern.StartsWith("support/admin", StringComparison.Ordinal) && !pattern.StartsWith("support/admin/settings/two-factor", StringComparison.Ordinal)
                ? Prefix + pattern["support/admin".Length..]
                : pattern;
            return $"{moved} {string.Join(",", e.GetProperty("methods").EnumerateArray().Select(m => m.GetString()))} {e.GetProperty("action").GetString()}";
        }).OrderBy(x => x, StringComparer.Ordinal).ToList();

        var live = ((List<Dictionary<string, object?>>)DefaultRouteTableTests.Snapshot(host)["endpoints"]!)
            .Select(e => $"{e["pattern"]} {string.Join(",", (List<string>)e["methods"]!)} {e["action"]}")
            .OrderBy(x => x, StringComparer.Ordinal).ToList();

        Assert.Equal(expected, live);
        Assert.Contains(live, l => l.StartsWith("backoffice/helpdesk/plugins/{slug}/{**route} GET", StringComparison.Ordinal));
        Assert.Contains(live, l => l.StartsWith("backoffice/helpdesk/api-tokens GET", StringComparison.Ordinal));
        Assert.Contains(live, l => l.StartsWith("support/admin/settings/two-factor GET", StringComparison.Ordinal));
        Assert.Contains(live, l => l.StartsWith("support/agent GET", StringComparison.Ordinal));
        Assert.Contains(live, l => l.StartsWith("admin/newsletters GET", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EmbeddedEndpointsCarryTheHostPolicyAndKeepTheAdminPolicy()
    {
        await using var host = await StartAsync();

        var tags = ((List<Dictionary<string, object?>>)DefaultRouteTableTests.Snapshot(host)["endpoints"]!)
            .Single(e => (string)e["pattern"]! == "backoffice/helpdesk/tags" && ((List<string>)e["methods"]!).SequenceEqual(new[] { "GET" })
                && ((string)e["action"]!).EndsWith("AdminPageController.Tags", StringComparison.Ordinal));

        var authorize = (List<string>)tags["authorize"]!;
        Assert.Contains($"policy={EscalatedPolicies.Admin};roles=;schemes=", authorize);
        Assert.Contains($"policy={HostPolicy};roles=;schemes=", authorize);
        Assert.Contains(typeof(EscalatedEmbeddedAdminMetadata).FullName!, (List<string>)tags["metadata"]!);
        Assert.Contains((List<string>)tags["filters"]!, f => f.Contains("EnforceRoutePermissionsFilter", StringComparison.Ordinal));
        Assert.Contains((List<string>)tags["filters"]!, f => f.Contains(nameof(HostHeaderFilter), StringComparison.Ordinal));
    }

    [Fact]
    public async Task LeavesTheAgentPanelCustomerPagesAndTwoFactorEnrolmentWhereTheyWere()
    {
        await using var host = await StartAsync(options: o => o.Access.TwoFactor = true);
        await host.GrantRoleAsync(Admin, "escalated-agent");
        var admin = host.ClientFor(Admin, HostClaim);

        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(Visit(HttpMethod.Get, "/support/agent"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(Visit(HttpMethod.Get, "/support/admin/settings/two-factor"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.SendAsync(Visit(HttpMethod.Get, "/backoffice/helpdesk/settings/two-factor"))).StatusCode);
    }

    [Fact]
    public async Task TheRouteHelperFollowsTheMove()
    {
        await using var host = await StartAsync();

        var response = await host.ClientFor(Admin, HostClaim).SendAsync(Browse("/backoffice/helpdesk/tags"));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"escalated.admin.tags.index\":\"/backoffice/helpdesk/tags\"", html);
        Assert.Contains("\"escalated.admin.plugins.index\":\"/backoffice/helpdesk/plugins\"", html);
        Assert.Contains("\"escalated.admin.api-tokens.index\":\"/backoffice/helpdesk/api-tokens\"", html);
        Assert.Contains("\"escalated.admin.two-factor.index\":\"/support/admin/settings/two-factor\"", html);
        Assert.Contains("\"escalated.agent.dashboard\":\"/support/agent\"", html);
    }

    [Fact]
    public async Task FormsRedirectUnderTheHostPrefix()
    {
        await using var host = await StartAsync();

        var response = await host.ClientFor(Admin, HostClaim).SendAsync(Visit(HttpMethod.Post, "/backoffice/helpdesk/tags", new { name = "Billing" }));

        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        Assert.Equal("/backoffice/helpdesk/tags", response.Headers.Location!.OriginalString);
        Assert.True(await host.QueryAsync(db => db.Tags.AnyAsync(t => t.Name == "Billing")));
    }

    [Fact]
    public async Task PageUrlsInPropsFollowTheMove()
    {
        await using var host = await StartAsync();

        var page = await PageAsync(host.ClientFor(Admin, HostClaim), "/backoffice/helpdesk/settings");

        Assert.Equal("/backoffice/helpdesk/settings", page.GetProperty("props").GetProperty("update_url").GetString());
    }

    [Fact]
    public async Task SharesTheEmbedFlagOnEmbeddedPages()
    {
        await using var host = await StartAsync();

        var page = await PageAsync(host.ClientFor(Admin, HostClaim), "/backoffice/helpdesk/tags");

        Assert.Equal(
            """{"enabled":true,"mode":"layout","layout":"Layouts/AdminLayout","route":"escalated.admin.tags.index","base_url":"/backoffice/helpdesk","allowed_origins":[]}""",
            SharedProps(page).GetProperty("embed").GetRawText());
    }

    [Fact]
    public async Task MarksOtherPagesAsNotEmbeddedButStillSharesTheMenu()
    {
        await using var host = await StartAsync();
        await host.GrantRoleAsync(Admin, "escalated-agent");

        var escalated = SharedProps(await PageAsync(host.ClientFor(Admin, HostClaim), "/support/agent"));

        Assert.False(escalated.GetProperty("embed").GetProperty("enabled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, escalated.GetProperty("embed").GetProperty("route").ValueKind);
        Assert.Equal("/backoffice/helpdesk", escalated.GetProperty("embed").GetProperty("base_url").GetString());
        Assert.True(escalated.GetProperty("navigation").GetArrayLength() > 0);
    }

    [Fact]
    public async Task AddsNoFramingHeadersInLayoutMode()
    {
        await using var host = await StartAsync();

        var response = await host.ClientFor(Admin, HostClaim).SendAsync(Visit(HttpMethod.Get, "/backoffice/helpdesk/tags"));

        Assert.False(response.Headers.Contains("Content-Security-Policy"));
        Assert.Equal("SAMEORIGIN", response.Headers.GetValues("X-Frame-Options").Single());
    }

    [Fact]
    public async Task RoutePermissionsStillApply()
    {
        await using var host = await StartAsync(options: o => o.Access.RolesPermissions = true);
        using (var scope = host.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<PermissionSeeder>().SeedAsync();
        }

        var admin = host.ClientFor(Admin, HostClaim);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.SendAsync(Visit(HttpMethod.Get, "/backoffice/helpdesk/tags"))).StatusCode);

        await host.GrantRoleAsync(Admin, await RoleAsync(host, "tag.view"));
        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(Visit(HttpMethod.Get, "/backoffice/helpdesk/tags"))).StatusCode);
    }

    [Fact]
    public async Task TwoFactorStillApplies()
    {
        await using var host = await StartAsync(options: o => o.Access.TwoFactor = true);
        await host.SeedAsync(async db =>
        {
            db.Settings.Add(new EscalatedSettings { Key = "two_factor_required", Value = "1" });
            await db.SaveChangesAsync();
        });

        var response = await host.ClientFor(Admin, HostClaim).GetAsync("/backoffice/helpdesk/tags");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/support/admin/settings/two-factor", response.Headers.Location!.OriginalString);
    }

    /// <summary>A role holding exactly <paramref name="slugs"/>.</summary>
    internal static async Task<string> RoleAsync(EscalatedTestHost host, params string[] slugs)
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
}
