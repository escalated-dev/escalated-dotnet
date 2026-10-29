using System.Net;
using System.Text;
using System.Text.Json;
using Escalated.Models;
using Xunit;
using static Escalated.Tests.Hosting.InertiaTestSupport;

namespace Escalated.Tests.Plugins;

/// <summary>The plugin contract's port test checklist, "Pages, endpoints, webhooks", over HTTP.</summary>
public class PluginPagesAndEndpointsTests
{
    private const string Base = "/support/admin/plugins/" + CrmPlugin.Slug;
    private const string Api = "/support/api/plugins/" + CrmPlugin.Slug;
    private const string Hooks = "/support/webhooks/plugins/" + CrmPlugin.Slug;

    private static async Task GrantCapabilityAsync(PluginHost plugins, string user, string capability) =>
        await plugins.Host.SeedAsync(async db =>
        {
            var permission = new Permission { Name = capability, Slug = capability, Group = "plugins" };
            var role = new Role { Name = "CRM viewers", Slug = "crm-viewers" };
            db.AddRange(permission, role);
            await db.SaveChangesAsync();
            db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id });
            db.RoleUsers.Add(new RoleUser { RoleId = role.Id, UserId = user });
            await db.SaveChangesAsync();
        });

    [Fact]
    public async Task AnActivePluginsPageRendersWithPropsFromItsOwnEndpoint()
    {
        await using var plugins = await PluginHost.StartAsync();
        await plugins.ActivateAsync(CrmPlugin.Slug);

        var page = await PageAsync(plugins.AdminClient, Base + "/dashboard");

        AssertPage(page, "Escalated/Plugin/Page");
        var props = page.GetProperty("props");
        Assert.Equal(CrmPlugin.Name, props.GetProperty("plugin").GetString());
        Assert.Equal("CrmDashboard", props.GetProperty("component").GetString());
        Assert.Equal("admin", props.GetProperty("layout").GetString());
        Assert.Equal(4, props.GetProperty("props").GetProperty("open_deals").GetInt32());

        var plain = (await PageAsync(plugins.AdminClient, Base + "/plain")).GetProperty("props");
        Assert.Equal("bare", plain.GetProperty("layout").GetString());
        Assert.Equal(JsonValueKind.Object, plain.GetProperty("props").ValueKind);
        Assert.Empty(plain.GetProperty("props").EnumerateObject());
    }

    [Fact]
    public async Task AnInactivePluginsPageIsA404AndNonAdminsAreRefused()
    {
        await using var plugins = await PluginHost.StartAsync();

        var inactive = await plugins.AdminClient.SendAsync(Visit(HttpMethod.Get, Base + "/dashboard"));
        Assert.Equal(HttpStatusCode.NotFound, inactive.StatusCode);

        await plugins.ActivateAsync(CrmPlugin.Slug);
        var unknownPage = await plugins.AdminClient.SendAsync(Visit(HttpMethod.Get, Base + "/nowhere"));
        Assert.Equal(HttpStatusCode.NotFound, unknownPage.StatusCode);
        var agent = await plugins.AgentClient.SendAsync(Visit(HttpMethod.Get, Base + "/dashboard"));
        Assert.Equal(HttpStatusCode.Forbidden, agent.StatusCode);
        Assert.DoesNotContain("Escalated/Plugin/Page", await agent.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task APageOrEndpointWithACapabilityTheUserLacksIsA403()
    {
        await using var plugins = await PluginHost.StartAsync();
        await plugins.ActivateAsync(CrmPlugin.Slug);

        var page = await plugins.AdminClient.SendAsync(Visit(HttpMethod.Get, Base + "/secret"));
        var endpoint = await plugins.AdminClient.SendAsync(PluginHost.Script(HttpMethod.Get, Api + "/guarded"));
        Assert.Equal(HttpStatusCode.Forbidden, page.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, endpoint.StatusCode);

        await GrantCapabilityAsync(plugins, PluginHost.Admin, "crm.view");
        Assert.Equal("CrmSecret", (await PageAsync(plugins.AdminClient, Base + "/secret")).GetProperty("props").GetProperty("component").GetString());
        Assert.Equal(HttpStatusCode.OK, (await plugins.AdminClient.SendAsync(PluginHost.Script(HttpMethod.Get, Api + "/guarded"))).StatusCode);
    }

    [Fact]
    public async Task AnEndpointGetsItsParamsQueryAndBodyAndNoCredentials()
    {
        await using var plugins = await PluginHost.StartAsync();
        await plugins.ActivateAsync(CrmPlugin.Slug);

        var request = PluginHost.Script(HttpMethod.Post, Api + "/contacts/42?full=1", new { qty = 2 });
        request.Headers.Add("Cookie", "session=secret");
        request.Headers.Add("Authorization", "Bearer secret");
        request.Headers.Add("X-CSRF-TOKEN", "secret");
        request.Headers.Add("Proxy-Authorization", "secret");
        var response = await plugins.AdminClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("yes", response.Headers.GetValues("x-crm").Single());
        var body = await PluginHost.JsonAsync(response);
        Assert.Equal("/contacts/:id", body.GetProperty("path").GetString());
        Assert.Equal("42", body.GetProperty("params").GetProperty("id").GetString());
        Assert.Equal("1", body.GetProperty("query").GetProperty("full").GetString());
        Assert.Equal(2, body.GetProperty("body").GetProperty("qty").GetInt32());
        var headers = body.GetProperty("header_names").EnumerateArray().Select(h => h.GetString()).ToList();
        Assert.Contains("x-requested-with", headers);
        Assert.DoesNotContain("cookie", headers);
        Assert.DoesNotContain("authorization", headers);
        Assert.DoesNotContain("x-csrf-token", headers);
        Assert.DoesNotContain("proxy-authorization", headers);
        Assert.Equal(PluginHost.Admin, body.GetProperty("user").GetProperty("id").GetString());
    }

    [Fact]
    public async Task EndpointsAreForAdminsOnActivePluginsAndUnsafeCallsComeFromAPageScript()
    {
        await using var plugins = await PluginHost.StartAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await plugins.AdminClient.SendAsync(PluginHost.Script(HttpMethod.Get, Api + "/guarded"))).StatusCode);
        await plugins.ActivateAsync(CrmPlugin.Slug);

        Assert.Equal(HttpStatusCode.Forbidden, (await plugins.AgentClient.SendAsync(PluginHost.Script(HttpMethod.Post, Api + "/contacts/1"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await plugins.AdminClient.SendAsync(PluginHost.Script(HttpMethod.Get, Api + "/contacts/1"))).StatusCode);

        var crossSite = new HttpRequestMessage(HttpMethod.Post, Api + "/contacts/1") { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        Assert.Equal(HttpStatusCode.Forbidden, (await plugins.AdminClient.SendAsync(crossSite)).StatusCode);
        Assert.DoesNotContain(plugins.Recorder.CallsOf(CrmPlugin.Name), c => c.StartsWith("endpoint:POST", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AWebhookAnswersWithoutAuthAndAnInactivePluginsWebhookIsA404()
    {
        await using var plugins = await PluginHost.StartAsync();
        var payload = new StringContent("{\"event\":\"paid\"}", Encoding.UTF8, "application/json");

        Assert.Equal(HttpStatusCode.NotFound, (await plugins.Host.Client.PostAsync(Hooks + "/inbound", payload)).StatusCode);
        await plugins.ActivateAsync(CrmPlugin.Slug);

        var response = await plugins.Host.Client.PostAsync(Hooks + "/inbound", new StringContent("{\"event\":\"paid\"}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await PluginHost.JsonAsync(response);
        Assert.True(body.GetProperty("received").GetBoolean());
        Assert.Equal("{\"event\":\"paid\"}", Encoding.UTF8.GetString(Convert.FromBase64String(body.GetProperty("raw").GetString()!)));
        Assert.Equal(HttpStatusCode.NotFound, (await plugins.Host.Client.PostAsync(Hooks + "/other", new StringContent(""))).StatusCode);
    }

    [Fact]
    public async Task AThrowingEndpointIsA500WithoutTheErrorMessage()
    {
        await using var plugins = await PluginHost.StartAsync();
        await plugins.ActivateAsync(CrmPlugin.Slug);

        var response = await plugins.AdminClient.SendAsync(PluginHost.Script(HttpMethod.Get, Api + "/boom"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("kaboom", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PluginsShareMenusSlotsAndWidgetsOnlyWhileActive()
    {
        await using var plugins = await PluginHost.StartAsync();

        var before = await PageAsync(plugins.AdminClient, "/support/admin/plugins");
        Assert.Empty(before.GetProperty("props").GetProperty("escalated").GetProperty("plugins").EnumerateObject());

        await plugins.ActivateAsync(CrmPlugin.Slug);
        var shared = (await PageAsync(plugins.AdminClient, "/support/admin/plugins")).GetProperty("props").GetProperty("escalated").GetProperty("plugins");

        var menu = shared.GetProperty("menuItems").EnumerateArray().Single();
        Assert.Equal("CRM", menu.GetProperty("label").GetString());
        Assert.Equal(Base + "/dashboard", menu.GetProperty("route").GetString());
        Assert.Equal("users", menu.GetProperty("icon").GetString());
        Assert.Equal(5, menu.GetProperty("position").GetInt32());
        Assert.Equal("admin", menu.GetProperty("section").GetString());
        Assert.Equal(CrmPlugin.Slug, menu.GetProperty("plugin").GetString());

        var slot = shared.GetProperty("pageComponents").GetProperty("ticket.show.sidebar").EnumerateArray().Single();
        Assert.Equal(CrmPlugin.Slug, slot.GetProperty("plugin").GetString());
        Assert.Equal("CrmCard", slot.GetProperty("component").GetString());
        Assert.True(slot.GetProperty("data").GetProperty("compact").GetBoolean());
        Assert.Equal(3, slot.GetProperty("position").GetInt32());

        var widget = shared.GetProperty("dashboardWidgets").EnumerateArray().Single();
        Assert.Equal($"{CrmPlugin.Slug}.CrmStats", widget.GetProperty("id").GetString());
        Assert.Equal("CRM stats", widget.GetProperty("title").GetString());
        Assert.Equal("full", widget.GetProperty("width").GetString());
        Assert.Equal(2, widget.GetProperty("position").GetInt32());

        // An agent sees no admin menu items and nothing behind a capability they lack.
        var agentShared = (await PageAsync(plugins.AgentClient, "/support/agent")).GetProperty("props").GetProperty("escalated").GetProperty("plugins");
        Assert.Empty(agentShared.GetProperty("menuItems").EnumerateArray());
        Assert.Single(agentShared.GetProperty("pageComponents").GetProperty("ticket.show.sidebar").EnumerateArray());

        await GrantCapabilityAsync(plugins, PluginHost.Agent, "crm.view");
        var granted = (await PageAsync(plugins.AgentClient, "/support/agent")).GetProperty("props").GetProperty("escalated").GetProperty("plugins");
        Assert.Equal("Secret", granted.GetProperty("menuItems").EnumerateArray().Single().GetProperty("label").GetString());
        Assert.Equal(2, granted.GetProperty("pageComponents").GetProperty("ticket.show.sidebar").GetArrayLength());
    }
}
