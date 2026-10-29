using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Escalated.Configuration;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Escalated.Tests.Hosting.InertiaTestSupport;

namespace Escalated.Tests.Plugins;

/// <summary>
/// SDK plugins behind the bridge, over a real subprocess: this test assembly run as
/// <see cref="FakePluginRuntime"/>, which logs every message the host sends it.
/// </summary>
public sealed class PluginBridgeTests : IDisposable
{
    private const string Slug = FakePluginRuntime.Slug;
    private const string Name = FakePluginRuntime.Name;

    private readonly string _log = Path.Combine(Path.GetTempPath(), $"escalated-dotnet-runtime-{Guid.NewGuid():N}.log");

    public void Dispose()
    {
        try
        {
            File.Delete(_log);
        }
        catch (IOException)
        {
            // Still held by a runtime that is being killed; the temp directory takes it.
        }
    }

    internal static void UseFakeRuntime(EscalatedOptions options, params string[] arguments)
    {
        options.Plugins.Runtime.Enabled = true;
        options.Plugins.Runtime.Command = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host ? host : "dotnet";
        options.Plugins.Runtime.Arguments = new List<string> { typeof(FakePluginRuntime).Assembly.Location, FakePluginRuntime.Flag }
            .Concat(arguments).ToList();
    }

    private Task<PluginHost> StartAsync(params string[] flags) =>
        PluginHost.StartAsync(options: o => UseFakeRuntime(o, new[] { _log }.Concat(flags).ToArray()));

    /// <summary>The params of every message of one method the host sent the runtime.</summary>
    private List<JsonObject> Sent(string method)
    {
        if (!File.Exists(_log)) return new List<JsonObject>();
        using var stream = new FileStream(_log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonNode.Parse(line)!.AsObject())
            .Where(m => m["method"]!.GetValue<string>() == method)
            .Select(m => m["params"]!.AsObject())
            .ToList();
    }

    [Fact]
    public async Task SdkPluginsAreListedAsPackageManagedNextToNativeOnes()
    {
        await using var plugins = await StartAsync();

        var page = await PageAsync(plugins.AdminClient, "/support/admin/plugins");

        var listed = page.GetProperty("props").GetProperty("plugins").EnumerateArray().ToDictionary(p => p.GetProperty("slug").GetString()!);
        Assert.Contains(CrmPlugin.Slug, listed.Keys);
        var sdk = listed[Slug];
        Assert.Equal(Name, sdk.GetProperty("name").GetString());
        Assert.Equal("0.4.0", sdk.GetProperty("version").GetString());
        Assert.Equal("composer", sdk.GetProperty("source").GetString());
        Assert.False(sdk.GetProperty("is_active").GetBoolean());
        Assert.Equal(1, sdk.GetProperty("provides").GetProperty("menu_items").GetInt32());
        Assert.Equal(1, sdk.GetProperty("provides").GetProperty("widgets").GetInt32());

        var handshake = Sent("handshake").Single();
        Assert.Equal("1.0", handshake["protocol_version"]!.GetValue<string>());
        Assert.Equal("dotnet", handshake["host"]!.GetValue<string>());
    }

    [Fact]
    public async Task ActivationSendsActivateAndThePluginActivatedActionWithTheActivePlugins()
    {
        await using var plugins = await StartAsync();

        await plugins.ActivateAsync(Slug);
        Assert.True((await plugins.RowAsync(Slug))!.IsActive);

        Assert.Equal(Name, Sent("activate").Single()["plugin"]!.GetValue<string>());
        var action = Sent("action").Single();
        Assert.Equal("plugin.activated", action["hook"]!.GetValue<string>());
        Assert.Equal(Name, action["event"]!["plugin"]!.GetValue<string>());
        Assert.Equal(Slug, action["event"]!["slug"]!.GetValue<string>());
        Assert.Equal(new[] { Name }, action["plugins"]!.AsArray().Select(n => n!.GetValue<string>()));

        var response = await plugins.AdminClient.SendAsync(Visit(HttpMethod.Post, $"/support/admin/plugins/{Slug}/deactivate"));
        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        Assert.Equal(Name, Sent("deactivate").Single()["plugin"]!.GetValue<string>());
        Assert.False((await plugins.RowAsync(Slug))!.IsActive);
    }

    [Fact]
    public async Task APackageManagedPluginCannotBeUninstalledHere()
    {
        await using var plugins = await StartAsync();
        await plugins.ActivateAsync(Slug);

        var response = await plugins.AdminClient.SendAsync(Visit(HttpMethod.Delete, $"/support/admin/plugins/{Slug}"));

        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        var page = await PageAsync(plugins.AdminClient, "/support/admin/plugins", FlashCookie(response));
        Assert.NotNull(page.GetProperty("props").GetProperty("flash").GetProperty("error").GetString());
        Assert.True((await plugins.RowAsync(Slug))!.IsActive);
    }

    [Fact]
    public async Task PagesEndpointsAndWebhooksGoThroughTheRuntime()
    {
        await using var plugins = await StartAsync();
        await plugins.ActivateAsync(Slug);

        var page = (await PageAsync(plugins.AdminClient, $"/support/admin/plugins/{Slug}/dash")).GetProperty("props");
        Assert.Equal(Name, page.GetProperty("plugin").GetString());
        Assert.Equal("SdkDash", page.GetProperty("component").GetString());
        Assert.Equal(3, page.GetProperty("props").GetProperty("widgets").GetInt32());

        var request = PluginHost.Script(HttpMethod.Post, $"/support/api/plugins/{Slug}/items/7?full=1", new { qty = 2 });
        request.Headers.Add("Authorization", "Bearer secret");
        var response = await plugins.AdminClient.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("fixture", response.Headers.GetValues("x-runtime").Single());
        var body = await PluginHost.JsonAsync(response);
        Assert.Equal("/items/:id", body.GetProperty("path").GetString());
        Assert.Equal("7", body.GetProperty("params").GetProperty("id").GetString());
        Assert.Equal("1", body.GetProperty("query").GetProperty("full").GetString());
        Assert.Equal(2, body.GetProperty("body").GetProperty("qty").GetInt32());
        var names = body.GetProperty("header_names").EnumerateArray().Select(h => h.GetString()).ToList();
        Assert.DoesNotContain("authorization", names);
        Assert.DoesNotContain("cookie", names);

        var sent = Sent("endpoint").Last();
        Assert.Equal(Name, sent["plugin"]!.GetValue<string>());
        Assert.Equal(1, sent["httpContract"]!.GetValue<int>());
        Assert.Equal("{\"qty\":2}", Encoding.UTF8.GetString(Convert.FromBase64String(sent["rawBodyBase64"]!.GetValue<string>())));

        var webhook = await plugins.Host.Client.PostAsync($"/support/webhooks/plugins/{Slug}/hook", new StringContent("{\"ping\":true}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Created, webhook.StatusCode);
        Assert.Equal("webhook", (await PluginHost.JsonAsync(webhook)).GetProperty("kind").GetString());
    }

    [Fact]
    public async Task TheRuntimesCtxCallsAreScopedToThePluginBeingServed()
    {
        await using var plugins = await StartAsync();
        await plugins.ActivateAsync(Slug);

        var response = await plugins.AdminClient.SendAsync(PluginHost.Script(HttpMethod.Get, $"/support/api/plugins/{Slug}/peek"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await PluginHost.JsonAsync(response);
        Assert.Equal("from-sdk", body.GetProperty("stored").GetString());
        Assert.Equal(PluginHost.Admin, body.GetProperty("user").GetProperty("id").GetString());
        var rows = await plugins.Host.QueryAsync(db => db.PluginStore.Where(r => r.Collection == "notes").ToListAsync());
        Assert.Equal(Name, Assert.Single(rows).Plugin);
    }

    [Fact]
    public async Task AnInactiveSdkPluginIsNeverCalled()
    {
        await using var plugins = await StartAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await plugins.AdminClient.SendAsync(PluginHost.Script(HttpMethod.Get, $"/support/api/plugins/{Slug}/peek"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await plugins.Host.Client.PostAsync($"/support/webhooks/plugins/{Slug}/hook", new StringContent("{}"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await plugins.AdminClient.SendAsync(Visit(HttpMethod.Get, $"/support/admin/plugins/{Slug}/dash"))).StatusCode);

        await plugins.ActivateAsync(CrmPlugin.Slug);
        var created = await plugins.CustomerClient.SendAsync(Visit(HttpMethod.Post, "/support", new { subject = "Quiet", description = "Nobody listens." }));
        Assert.Equal(HttpStatusCode.SeeOther, created.StatusCode);
        await plugins.IdleAsync();

        Assert.Empty(Sent("endpoint"));
        Assert.Empty(Sent("webhook"));
        Assert.Empty(Sent("action"));
        Assert.Empty(Sent("activate"));
    }

    [Fact]
    public async Task ATicketCreatedOverHttpReachesTheSdkPluginWhichCanWriteBack()
    {
        await using var plugins = await StartAsync();
        await plugins.ActivateAsync(Slug);

        var created = await plugins.CustomerClient.SendAsync(Visit(HttpMethod.Post, "/support", new { subject = "Bridge me", description = "Please." }));
        Assert.Equal(HttpStatusCode.SeeOther, created.StatusCode);
        await plugins.IdleAsync();

        var action = Sent("action").Single(a => a["hook"]!.GetValue<string>() == "ticket.created");
        Assert.Equal("Bridge me", action["event"]!["subject"]!.GetValue<string>());
        Assert.Equal("Bridge me", action["event"]!["ticket"]!["title"]!.GetValue<string>());
        Assert.Equal(new[] { Name }, action["plugins"]!.AsArray().Select(n => n!.GetValue<string>()));
        var seen = await plugins.Host.QueryAsync(db => db.PluginStore.Where(r => r.Plugin == Name && r.Collection == "seen").ToListAsync());
        Assert.Equal(action["event"]!["id"]!.GetValue<int>(), JsonNode.Parse(Assert.Single(seen).Data!)!["ticket"]!.GetValue<int>());
    }

    [Fact]
    public async Task FiltersRunNativeAndSdkPluginsByPriority()
    {
        await using var plugins = await StartAsync();
        await plugins.ActivateAsync(CrmPlugin.Slug);
        await plugins.ActivateAsync(Slug);

        var value = await plugins.WithRegistryAsync(r => r.ApplyFilterAsync("demo.value", JsonValue.Create("x")));

        Assert.Equal("x|sdk|crm", value!.GetValue<string>()); // sdk (15) before crm (20)
        Assert.Equal(new[] { Name }, Sent("filter").Single()["plugins"]!.AsArray().Select(n => n!.GetValue<string>()));
    }

    [Fact]
    public async Task AThrowingSdkEndpointIsA500WithoutTheErrorAndTheRuntimeStaysUp()
    {
        await using var plugins = await StartAsync();
        await plugins.ActivateAsync(Slug);

        var response = await plugins.AdminClient.SendAsync(PluginHost.Script(HttpMethod.Get, $"/support/api/plugins/{Slug}/boom"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("kaboom", await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await plugins.AdminClient.SendAsync(PluginHost.Script(HttpMethod.Get, $"/support/api/plugins/{Slug}/peek"))).StatusCode);
        Assert.Single(Sent("handshake"));
    }

    [Fact]
    public async Task ARuntimeThatCannotStartLeavesTheHostServing()
    {
        await using var plugins = await StartAsync("--die");

        var page = await PageAsync(plugins.AdminClient, "/support/admin/plugins");
        var listed = page.GetProperty("props").GetProperty("plugins").EnumerateArray().Select(p => p.GetProperty("slug").GetString()).ToList();
        Assert.Contains(CrmPlugin.Slug, listed);
        Assert.DoesNotContain(Slug, listed);

        await plugins.ActivateAsync(CrmPlugin.Slug);
        var created = await plugins.CustomerClient.SendAsync(Visit(HttpMethod.Post, "/support", new { subject = "No node", description = "Fine." }));
        Assert.Equal(HttpStatusCode.SeeOther, created.StatusCode);
        await plugins.IdleAsync();
        Assert.Single(plugins.Recorder.Actions(CrmPlugin.Name, "ticket.created"));
    }

    [Fact]
    public async Task ARuntimeThatDiesAnswers503AndTheRequestIsUnaffected()
    {
        await using var plugins = await StartAsync("--die-on-endpoint");
        await plugins.ActivateAsync(Slug);

        var response = await plugins.AdminClient.SendAsync(PluginHost.Script(HttpMethod.Post, $"/support/api/plugins/{Slug}/items/1", new { }));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var created = await plugins.CustomerClient.SendAsync(Visit(HttpMethod.Post, "/support", new { subject = "After the crash", description = "Still here." }));
        Assert.Equal(HttpStatusCode.SeeOther, created.StatusCode);
        await plugins.IdleAsync();
        Assert.Equal(1, await plugins.Host.QueryAsync(db => db.Tickets.CountAsync()));
    }
}
