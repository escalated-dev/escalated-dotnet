using System.Net;
using Escalated.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using static Escalated.Tests.Embed.EmbedTestSupport;
using static Escalated.Tests.Hosting.InertiaTestSupport;

namespace Escalated.Tests.Embed;

/// <summary>
/// The embedded admin's gate replacement, iframe framing headers and configuration (ADR
/// 2026-11-05-embeddable-admin).
/// </summary>
public class EmbeddedAdminGateAndFramingTests
{
    private static Action<EscalatedOptions> ReplaceGate => o =>
        o.EmbedAdmin(prefix: Prefix, policies: new[] { HostPolicy }, replaceAdminGate: true);

    [Fact]
    public async Task WithTheGateReplacedTheHostPolicyAloneDecides()
    {
        await using var host = await StartAsync(ReplaceGate);

        Assert.Equal(HttpStatusCode.OK, (await host.ClientFor(Plain, HostClaim).SendAsync(Visit(HttpMethod.Get, "/backoffice/helpdesk/tags"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.ClientFor(Agent, HostClaim).SendAsync(Visit(HttpMethod.Get, "/backoffice/helpdesk/tags"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.ClientFor(Admin).GetAsync("/backoffice/helpdesk/tags")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync("/backoffice/helpdesk/tags")).StatusCode);
    }

    [Fact]
    public async Task WithTheGateReplacedTheAgentPanelKeepsItsGateAndTheMenuIsBuiltForTheUser()
    {
        await using var host = await StartAsync(ReplaceGate);

        Assert.Equal(HttpStatusCode.Forbidden, (await host.ClientFor(Plain, HostClaim).GetAsync("/support/agent")).StatusCode);

        var page = await PageAsync(host.ClientFor(Plain, HostClaim), "/backoffice/helpdesk/tags");
        Assert.True(SharedProps(page).GetProperty("navigation").GetArrayLength() > 0);
        Assert.False(SharedProps(page).GetProperty("is_admin").GetBoolean());
    }

    [Fact]
    public async Task IframeModeAllowsTheListedOriginsOnEmbeddedResponsesOnly()
    {
        await using var host = await StartAsync(
            o => o.EmbedAdmin(prefix: Prefix, mode: EmbedOptions.IframeMode, frameAncestors: new[]
            {
                "https://allowed.example", "*", "https://*.example.org", "ftp://files.example", "https://a.example/path",
                "https://user:secret@b.example", "https://allowed.example",
            }),
            hostCsp: true);
        await host.GrantRoleAsync(Admin, "escalated-agent");
        var admin = host.ClientFor(Admin);

        var embedded = await admin.SendAsync(Visit(HttpMethod.Get, "/backoffice/helpdesk/tags"));
        Assert.Equal(HttpStatusCode.OK, embedded.StatusCode);
        Assert.False(embedded.Headers.Contains("X-Frame-Options"));
        Assert.Equal(
            new[] { "default-src 'self'", "frame-ancestors 'self' https://allowed.example" },
            embedded.Headers.GetValues("Content-Security-Policy").ToArray());
        var embed = SharedProps(JsonOf(await embedded.Content.ReadAsStringAsync())).GetProperty("embed");
        Assert.Equal("iframe", embed.GetProperty("mode").GetString());
        Assert.Equal("""["https://allowed.example"]""", embed.GetProperty("allowed_origins").GetRawText());

        var firstVisit = await admin.SendAsync(Browse("/backoffice/helpdesk/tags"));
        Assert.False(firstVisit.Headers.Contains("X-Frame-Options"));
        Assert.Contains("frame-ancestors 'self' https://allowed.example", firstVisit.Headers.GetValues("Content-Security-Policy"));

        var other = await admin.SendAsync(Visit(HttpMethod.Get, "/support/agent"));
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
        Assert.Equal("SAMEORIGIN", other.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal(new[] { "default-src 'self'" }, other.Headers.GetValues("Content-Security-Policy").ToArray());
    }

    [Fact]
    public async Task IframeModeSendsEscalatedsOwnCookiesForAThirdPartyFrameOverHttps()
    {
        await using var iframe = await StartAsync(o => o.EmbedAdmin(prefix: Prefix, mode: EmbedOptions.IframeMode));
        var framed = await FlashCookieAsync(iframe);
        Assert.Contains("samesite=none", framed, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; secure", framed, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", framed, StringComparison.OrdinalIgnoreCase);

        await using var layout = await StartAsync(o => o.EmbedAdmin(prefix: Prefix));
        Assert.Contains("samesite=lax", await FlashCookieAsync(layout), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> FlashCookieAsync(Hosting.EscalatedTestHost host)
    {
        var client = host.ClientFor(Admin);
        client.BaseAddress = new Uri("https://localhost/");
        var response = await client.SendAsync(Visit(HttpMethod.Post, "/backoffice/helpdesk/tags", new { name = "Billing" }));
        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        return response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("escalated_flash=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task IframeModeWithNoOriginsAllowsOnlyTheAppItself()
    {
        await using var host = await StartAsync(o => o.EmbedAdmin(prefix: Prefix, mode: EmbedOptions.IframeMode));

        var response = await host.ClientFor(Admin).SendAsync(Visit(HttpMethod.Get, "/backoffice/helpdesk/tags"));

        Assert.Equal("frame-ancestors 'self'", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.False(response.Headers.Contains("X-Frame-Options"));
    }

    [Fact]
    public void EmbedAdminTurnsEmbeddingOnAndRejectsAnUnknownMode()
    {
        var options = new EscalatedOptions().EmbedAdmin(policies: new[] { "BackOffice" }, layout: "Layouts/AdminLayout");

        Assert.True(options.Ui.Embed.Enabled);
        Assert.Equal("admin/support", options.Ui.Embed.Prefix);
        Assert.Equal("layout", options.Ui.Embed.Mode);
        Assert.Equal(new[] { "BackOffice" }, options.Ui.Embed.Policies);
        Assert.Throws<ArgumentException>(() => new EscalatedOptions().EmbedAdmin(mode: "popup"));
    }

    [Fact]
    public void IsOffByDefault()
    {
        var embed = new EscalatedOptions().Ui.Embed;

        Assert.False(embed.Enabled);
        Assert.Equal("layout", embed.Mode);
        Assert.Equal("admin/support", embed.Prefix);
        Assert.Null(embed.Policies);
        Assert.False(embed.ReplaceAdminGate);
        Assert.Null(embed.Layout);
        Assert.Null(embed.RootView);
        Assert.Empty(embed.FrameAncestors);
    }

    [Fact]
    public void BindsFromConfigurationAndTheEnvironmentVariables()
    {
        var fromEnvironment = Options(new Dictionary<string, string?>
        {
            ["ESCALATED_EMBED"] = "true",
            ["ESCALATED_EMBED_MODE"] = "iframe",
            ["ESCALATED_EMBED_PREFIX"] = "backoffice/support",
            ["ESCALATED_EMBED_FRAME_ANCESTORS"] = "https://a.example, https://b.example",
        });
        Assert.True(fromEnvironment.Enabled);
        Assert.Equal("iframe", fromEnvironment.Mode);
        Assert.Equal("backoffice/support", fromEnvironment.Prefix);
        Assert.Equal(new[] { "https://a.example", "https://b.example" }, fromEnvironment.FrameAncestors);

        var fromSection = Options(new Dictionary<string, string?>
        {
            ["Escalated:Ui:Embed:Enabled"] = "true",
            ["Escalated:Ui:Embed:Prefix"] = "staff/helpdesk",
            ["Escalated:Ui:Embed:Policies:0"] = "BackOffice",
            ["Escalated:Ui:Embed:ReplaceAdminGate"] = "true",
            ["Escalated:Ui:Embed:Layout"] = "Layouts/AdminLayout",
            ["Escalated:Ui:Embed:RootView"] = "~/Views/Shared/SupportAdmin.cshtml",
            ["ESCALATED_EMBED_PREFIX"] = "ignored",
        });
        Assert.True(fromSection.Enabled);
        Assert.Equal("staff/helpdesk", fromSection.Prefix);
        Assert.Equal(new[] { "BackOffice" }, fromSection.Policies);
        Assert.True(fromSection.ReplaceAdminGate);
        Assert.Equal("Layouts/AdminLayout", fromSection.Layout);
        Assert.Equal("~/Views/Shared/SupportAdmin.cshtml", fromSection.RootView);

        Assert.False(Options(new Dictionary<string, string?>()).Enabled);
    }

    private static EmbedOptions Options(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddEscalated(configuration, _ => { });
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<EscalatedOptions>>().Value.Ui.Embed;
    }

    private static System.Text.Json.JsonElement JsonOf(string text) => System.Text.Json.JsonDocument.Parse(text).RootElement.Clone();
}
