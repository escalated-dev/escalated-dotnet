using System.Net;
using Escalated.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Escalated.Tests.Hosting.InertiaTestSupport;

namespace Escalated.Tests.Hosting;

/// <summary>
/// The optional public routes (widget, guest) exist only while their switch is on, and
/// <c>Escalated:Routes:CustomerPolicies</c> guards the customer portal and the knowledge base
/// (escalated-laravel's <c>escalated.widget.enabled</c>, <c>escalated.guest.routes_enabled</c> and
/// <c>escalated.routes.customer_middleware</c>).
/// </summary>
public class OptionalRoutesTests
{
    private const string StaffPolicy = "host-staff";
    private const string Customer = "customer-1";
    private const string Staff = "staff-1";

    private static List<string> Templates(EscalatedTestHost host) =>
        host.Endpoints.Select(e => e.RoutePattern.RawText ?? string.Empty).ToList();

    private static bool Under(IEnumerable<string> templates, string prefix) =>
        templates.Any(t => t == prefix || t.StartsWith(prefix + "/", StringComparison.Ordinal));

    [Fact]
    public async Task Defaults_RegisterTheWidgetAndGuestRoutes()
    {
        await using var host = await EscalatedTestHost.StartAsync(options => options.Ui.Enabled = true);
        var templates = Templates(host);

        Assert.True(Under(templates, "support/widget"));
        Assert.Contains("support/widget/embed.js", templates);
        Assert.True(Under(templates, "support/widget/chat"));
        Assert.True(Under(templates, "support/guest"));
        Assert.True(Under(templates, "support/api/v1/mobile/guest"));
    }

    [Fact]
    public async Task WidgetSwitchedOff_LeavesNoWidgetRoute()
    {
        await using var host = await EscalatedTestHost.StartAsync(options =>
        {
            options.Ui.Enabled = true;
            options.Widget.Enabled = false;
        });
        var templates = Templates(host);

        Assert.False(Under(templates, "support/widget"), string.Join("\n", templates.Where(t => t.StartsWith("support/widget"))));
        Assert.True(Under(templates, "support/guest"));
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync("/support/widget/config")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync("/support/widget/embed.js")).StatusCode);
    }

    [Fact]
    public async Task GuestRoutesSwitchedOff_LeavesNoGuestRoute()
    {
        await using var host = await EscalatedTestHost.StartAsync(options =>
        {
            options.Ui.Enabled = true;
            options.GuestAccess.RoutesEnabled = false;
        });
        var templates = Templates(host);

        Assert.False(Under(templates, "support/guest"), string.Join("\n", templates.Where(t => t.StartsWith("support/guest"))));
        Assert.False(Under(templates, "support/api/v1/mobile/guest"));
        Assert.True(Under(templates, "support/widget"));
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.SendAsync(Visit(HttpMethod.Get, "/support/guest/create"))).StatusCode);
    }

    [Fact]
    public void RouteSwitches_FallBackToTheirEnvironmentVariable()
    {
        Assert.True(new WidgetOptions().RoutesRegistered);
        Assert.True(new GuestAccessOptions().RoutesRegistered);
        Assert.False(new WidgetOptions { Enabled = false }.RoutesRegistered);

        var name = "ESCALATED_TEST_ROUTE_SWITCH_" + Guid.NewGuid().ToString("N");
        try
        {
            Assert.True(RouteSwitch.FromEnvironment(name));
            Environment.SetEnvironmentVariable(name, "false");
            Assert.False(RouteSwitch.FromEnvironment(name));
            Environment.SetEnvironmentVariable(name, "0");
            Assert.False(RouteSwitch.FromEnvironment(name));
            Environment.SetEnvironmentVariable(name, "true");
            Assert.True(RouteSwitch.FromEnvironment(name));
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    private static Task<EscalatedTestHost> StartWithPolicyAsync(bool guarded) => EscalatedTestHost.StartAsync(
        options =>
        {
            options.Ui.Enabled = true;
            if (guarded) options.Routes.CustomerPolicies.Add(StaffPolicy);
        },
        services => services.AddAuthorization(o => o.AddPolicy(StaffPolicy, p => p.RequireClaim("staff", "yes"))));

    public static IEnumerable<object[]> CustomerRoutes() => new[]
    {
        new object[] { "/support/tickets", false },
        new object[] { "/support", true },
        new object[] { "/support/create", true },
        new object[] { "/support/kb", true },
    };

    [Theory]
    [MemberData(nameof(CustomerRoutes))]
    public async Task CustomerPolicies_RefuseASignedInUserWhoFailsThem(string url, bool inertia)
    {
        await using var host = await StartWithPolicyAsync(guarded: true);

        Task<HttpResponseMessage> Get(HttpClient client) =>
            client.SendAsync(inertia ? Visit(HttpMethod.Get, url) : new HttpRequestMessage(HttpMethod.Get, url));

        Assert.Equal(HttpStatusCode.Forbidden, (await Get(host.ClientFor(Customer))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Get(host.ClientFor(Staff, ("staff", "yes")))).StatusCode);
    }

    [Fact]
    public async Task CustomerPolicies_MakeTheKnowledgeBaseNonPublic()
    {
        await using var host = await StartWithPolicyAsync(guarded: true);
        await host.SeedAsync(async db =>
        {
            db.Settings.Add(new Escalated.Models.EscalatedSettings { Key = "knowledge_base_public", Value = "1" });
            await db.SaveChangesAsync();
        });

        var response = await host.Client.SendAsync(Visit(HttpMethod.Get, "/support/kb"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(CustomerRoutes))]
    public async Task NoCustomerPolicies_LeaveTheCustomerRoutesAsTheyWere(string url, bool inertia)
    {
        await using var host = await StartWithPolicyAsync(guarded: false);

        var response = await host.ClientFor(Customer)
            .SendAsync(inertia ? Visit(HttpMethod.Get, url) : new HttpRequestMessage(HttpMethod.Get, url));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CustomerPolicies_LeaveStaffRoutesAlone()
    {
        await using var host = await StartWithPolicyAsync(guarded: true);
        await host.GrantRoleAsync("agent-1", "escalated-agent");

        var response = await host.ClientFor("agent-1").SendAsync(Visit(HttpMethod.Get, "/support/agent"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(host.Endpoints
            .Where(e => e.RoutePattern.RawText == "support/agent")
            .SelectMany(e => e.Metadata.GetOrderedMetadata<IAuthorizeData>())
            .FirstOrDefault(d => d.Policy == StaffPolicy));
    }
}
