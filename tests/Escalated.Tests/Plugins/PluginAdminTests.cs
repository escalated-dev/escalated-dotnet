using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Escalated.Models;
using Escalated.Plugins;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Escalated.Tests.Hosting.InertiaTestSupport;

namespace Escalated.Tests.Plugins;

/// <summary>
/// The plugin contract's port test checklist, "Admin screen", through the real
/// HTTP pipeline: Admin &gt; Plugins, activation, deactivation and uninstall.
/// </summary>
public class PluginAdminTests
{
    [Fact]
    public async Task TheIndexListsEveryPluginForAnAdmin()
    {
        await using var plugins = await PluginHost.StartAsync();

        var page = await PageAsync(plugins.AdminClient, "/support/admin/plugins");

        AssertPage(page, "Escalated/Admin/Plugins/Index");
        Assert.False(page.GetProperty("props").GetProperty("can_upload").GetBoolean());
        var listed = page.GetProperty("props").GetProperty("plugins").EnumerateArray().ToDictionary(p => p.GetProperty("slug").GetString()!);
        Assert.Equal(new[] { CrmPlugin.Slug, AuditPlugin.Slug }, listed.Keys.OrderBy(k => k, StringComparer.Ordinal));

        var crm = Keys(listed[CrmPlugin.Slug], "slug", "name", "description", "version", "author", "author_url", "homepage",
            "is_active", "activated_at", "installed_at", "source", "provides");
        Assert.Equal("Acme CRM", crm.GetProperty("name").GetString());
        Assert.Equal("2.1.0", crm.GetProperty("version").GetString());
        Assert.Equal("https://acme.test/crm", crm.GetProperty("homepage").GetString());
        Assert.Equal("https://acme.test/crm", crm.GetProperty("author_url").GetString());
        Assert.False(crm.GetProperty("is_active").GetBoolean());
        Assert.Equal("registered", crm.GetProperty("source").GetString());
        Assert.Equal(2, crm.GetProperty("provides").GetProperty("menu_items").GetInt32());
        Assert.Equal(1, crm.GetProperty("provides").GetProperty("widgets").GetInt32());
        Assert.Equal(2, crm.GetProperty("provides").GetProperty("page_components").GetInt32());
        Assert.DoesNotContain("config", crm.EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task TheIndexNeverShowsAPluginsConfig()
    {
        await using var plugins = await PluginHost.StartAsync();
        await plugins.ActivateAsync(CrmPlugin.Slug); // onActivate writes config

        var response = await plugins.AdminClient.SendAsync(Visit(HttpMethod.Get, "/support/admin/plugins"));

        Assert.DoesNotContain("activated\":true", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(PluginHost.Agent)]
    [InlineData(PluginHost.Customer)]
    [InlineData(null)]
    public async Task NonAdminsAreRefused(string? user)
    {
        await using var plugins = await PluginHost.StartAsync();
        var client = user is null ? plugins.Host.Client : plugins.Host.ClientFor(user);

        var index = await client.SendAsync(Visit(HttpMethod.Get, "/support/admin/plugins"));
        var activate = await client.SendAsync(Visit(HttpMethod.Post, $"/support/admin/plugins/{CrmPlugin.Slug}/activate"));

        Assert.Contains(index.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden });
        Assert.DoesNotContain("Escalated/Admin/Plugins/Index", await index.Content.ReadAsStringAsync());
        Assert.Contains(activate.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden });
        Assert.Null(await plugins.RowAsync(CrmPlugin.Slug));
    }

    [Fact]
    public async Task ActivateThenDeactivateRedirectsWithAFlashAndIsIdempotent()
    {
        await using var plugins = await PluginHost.StartAsync();
        var admin = plugins.AdminClient;

        var activated = await admin.SendAsync(Visit(HttpMethod.Post, $"/support/admin/plugins/{CrmPlugin.Slug}/activate"));
        Assert.Equal(HttpStatusCode.SeeOther, activated.StatusCode);
        Assert.Equal("/support/admin/plugins", activated.Headers.Location!.OriginalString);
        var flash = await PageAsync(admin, "/support/admin/plugins", FlashCookie(activated));
        Assert.Equal("Plugin activated.", flash.GetProperty("props").GetProperty("flash").GetProperty("success").GetString());

        var row = await plugins.RowAsync(CrmPlugin.Slug);
        Assert.True(row!.IsActive);
        Assert.NotNull(row.ActivatedAt);
        Assert.Null(row.DeactivatedAt);
        var listed = flash.GetProperty("props").GetProperty("plugins").EnumerateArray().Single(p => p.GetProperty("slug").GetString() == CrmPlugin.Slug);
        Assert.True(listed.GetProperty("is_active").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, listed.GetProperty("activated_at").ValueKind);

        var again = await admin.SendAsync(Visit(HttpMethod.Post, $"/support/admin/plugins/{CrmPlugin.Slug}/activate"));
        Assert.Equal(HttpStatusCode.SeeOther, again.StatusCode);
        Assert.Equal(1, plugins.Recorder.CallsOf(CrmPlugin.Name).Count(c => c == "onActivate"));
        Assert.Equal(row.ActivatedAt, (await plugins.RowAsync(CrmPlugin.Slug))!.ActivatedAt);

        var deactivated = await admin.SendAsync(Visit(HttpMethod.Post, $"/support/admin/plugins/{CrmPlugin.Slug}/deactivate"));
        Assert.Equal(HttpStatusCode.SeeOther, deactivated.StatusCode);
        var after = (await plugins.RowAsync(CrmPlugin.Slug))!;
        Assert.False(after.IsActive);
        Assert.NotNull(after.DeactivatedAt);
        var flashed = await PageAsync(admin, "/support/admin/plugins", FlashCookie(deactivated));
        Assert.Equal("Plugin deactivated.", flashed.GetProperty("props").GetProperty("flash").GetProperty("success").GetString());

        var twice = await admin.SendAsync(Visit(HttpMethod.Post, $"/support/admin/plugins/{CrmPlugin.Slug}/deactivate"));
        Assert.Equal(HttpStatusCode.SeeOther, twice.StatusCode);
        Assert.Equal(1, plugins.Recorder.CallsOf(CrmPlugin.Name).Count(c => c == "onDeactivate"));
    }

    [Fact]
    public async Task AnUnknownSlugIsAnErrorFlashOrA404AndCreatesNoRow()
    {
        await using var plugins = await PluginHost.StartAsync();
        var admin = plugins.AdminClient;

        var inertia = await admin.SendAsync(Visit(HttpMethod.Post, "/support/admin/plugins/nope/activate"));
        Assert.Equal(HttpStatusCode.SeeOther, inertia.StatusCode);
        var page = await PageAsync(admin, "/support/admin/plugins", FlashCookie(inertia));
        Assert.Equal("Plugin not found.", page.GetProperty("props").GetProperty("flash").GetProperty("error").GetString());

        var json = await admin.SendAsync(PluginHost.Script(HttpMethod.Post, "/support/admin/plugins/nope/deactivate"));
        Assert.Equal(HttpStatusCode.NotFound, json.StatusCode);
        var delete = await admin.SendAsync(PluginHost.Script(HttpMethod.Delete, "/support/admin/plugins/nope"));
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);

        Assert.Equal(0, await plugins.Host.QueryAsync(db => db.Plugins.CountAsync()));
    }

    [Fact]
    public async Task UninstallingARegisteredPluginKeepsItListedAndKeepsItsStore()
    {
        await using var plugins = await PluginHost.StartAsync();
        await plugins.ActivateAsync(CrmPlugin.Slug);
        Assert.Equal(1, await plugins.Host.QueryAsync(db => db.PluginStore.CountAsync(r => r.Plugin == CrmPlugin.Name)));

        var response = await plugins.AdminClient.SendAsync(Visit(HttpMethod.Delete, $"/support/admin/plugins/{CrmPlugin.Slug}"));

        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        Assert.Null(await plugins.RowAsync(CrmPlugin.Slug));
        var page = await PageAsync(plugins.AdminClient, "/support/admin/plugins", FlashCookie(response));
        Assert.Equal("Plugin uninstalled.", page.GetProperty("props").GetProperty("flash").GetProperty("success").GetString());
        var listed = page.GetProperty("props").GetProperty("plugins").EnumerateArray().Single(p => p.GetProperty("slug").GetString() == CrmPlugin.Slug);
        Assert.False(listed.GetProperty("is_active").GetBoolean());
        Assert.Equal(1, await plugins.Host.QueryAsync(db => db.PluginStore.CountAsync(r => r.Plugin == CrmPlugin.Name)));
        Assert.Equal(new[] { "onActivate", "onDeactivate" }, plugins.Recorder.CallsOf(CrmPlugin.Name).Where(c => c.StartsWith("on", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ThereIsNoUploadRoute()
    {
        await using var plugins = await PluginHost.StartAsync();

        var response = await plugins.AdminClient.SendAsync(Visit(HttpMethod.Post, "/support/admin/plugins/upload"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task LifecycleCallbacksAndActionsFireInOrderAndAThrowingCallbackDoesNotUndoIt()
    {
        await using var plugins = await PluginHost.StartAsync();
        await plugins.ActivateAsync(CrmPlugin.Slug);
        var admin = plugins.AdminClient;

        // AuditPlugin's onActivate throws; it is active all the same.
        var activated = await admin.SendAsync(Visit(HttpMethod.Post, $"/support/admin/plugins/{AuditPlugin.Slug}/activate"));
        Assert.Equal(HttpStatusCode.SeeOther, activated.StatusCode);
        Assert.True((await plugins.RowAsync(AuditPlugin.Slug))!.IsActive);

        var deactivated = await admin.SendAsync(Visit(HttpMethod.Post, $"/support/admin/plugins/{AuditPlugin.Slug}/deactivate"));
        Assert.Equal(HttpStatusCode.SeeOther, deactivated.StatusCode);
        Assert.False((await plugins.RowAsync(AuditPlugin.Slug))!.IsActive);

        await admin.SendAsync(Visit(HttpMethod.Post, $"/support/admin/plugins/{AuditPlugin.Slug}/activate"));
        var uninstalled = await admin.SendAsync(Visit(HttpMethod.Delete, $"/support/admin/plugins/{AuditPlugin.Slug}"));
        Assert.Equal(HttpStatusCode.SeeOther, uninstalled.StatusCode);
        Assert.Null(await plugins.RowAsync(AuditPlugin.Slug));

        Assert.Equal(new[]
        {
            "onActivate", "action:plugin.activated",
            "action:plugin.deactivated", "onDeactivate",
            "onActivate", "action:plugin.activated",
            "action:plugin.uninstalling", "action:plugin.deactivated", "onDeactivate",
        }, plugins.Recorder.CallsOf(AuditPlugin.Name));

        // Lifecycle actions reach every active plugin, with {plugin, slug}.
        var seen = plugins.Recorder.Actions(CrmPlugin.Name, "plugin.activated").Select(a => a!["slug"]!.GetValue<string>()).ToList();
        Assert.Equal(new[] { CrmPlugin.Slug, AuditPlugin.Slug, AuditPlugin.Slug }, seen);
        Assert.Equal(AuditPlugin.Name, plugins.Recorder.Actions(CrmPlugin.Name, "plugin.uninstalling").Single()!["plugin"]!.GetValue<string>());
    }

    [Fact]
    public async Task APackageManagedPluginCannotBeUninstalled()
    {
        await using var plugins = await PluginHost.StartAsync(natives: false,
            services: s => s.AddSingleton<Escalated.Plugins.Bridge.PluginBridge>(sp => new FakeManifestBridge(sp)));
        await plugins.ActivateAsync("acme--npm-plugin");

        var response = await plugins.AdminClient.SendAsync(Visit(HttpMethod.Delete, "/support/admin/plugins/acme--npm-plugin"));

        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        var page = await PageAsync(plugins.AdminClient, "/support/admin/plugins", FlashCookie(response));
        Assert.NotNull(page.GetProperty("props").GetProperty("flash").GetProperty("error").GetString());
        Assert.True((await plugins.RowAsync("acme--npm-plugin"))!.IsActive);
        Assert.Equal("composer", page.GetProperty("props").GetProperty("plugins")[0].GetProperty("source").GetString());
    }

    [Fact]
    public async Task TwoPluginsWithOneSlugFailTheHostAtStartupNamingBoth()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => PluginHost.StartAsync(services: s =>
            s.AddEscalatedPlugin<CrmLookalike>()));

        Assert.Contains(CrmPlugin.Name, error.Message);
        Assert.Contains("@acme/escalated.plugin-crm", error.Message);
    }

    [Fact]
    public void SlugsFollowTheContract()
    {
        Assert.Equal("escalated-dev--plugin-slack", PluginManifest.SlugFor("@escalated-dev/plugin-slack"));
        Assert.Equal("acme--billing-v2", PluginManifest.SlugFor("acme/billing v2"));
        Assert.Equal("a-b--c", PluginManifest.SlugFor("@a.b/c"));
    }

    private sealed class CrmLookalike : EscalatedPlugin
    {
        public override PluginManifest Manifest { get; } = new() { Name = "@acme/escalated.plugin-crm" };
    }

    /// <summary>A bridge whose runtime reports one SDK plugin and answers every call with null.</summary>
    private sealed class FakeManifestBridge : Escalated.Plugins.Bridge.PluginBridge
    {
        public FakeManifestBridge(IServiceProvider services)
            : base(services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Escalated.Configuration.EscalatedOptions>>(),
                services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Escalated.Plugins.Bridge.PluginBridge>>())
        {
        }

        public override Task<IReadOnlyDictionary<string, PluginManifest>> ManifestsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<string, PluginManifest>>(new Dictionary<string, PluginManifest>
            {
                ["@acme/npm-plugin"] = PluginManifest.FromJson(new JsonObject { ["name"] = "@acme/npm-plugin", ["version"] = "0.1.0" }),
            });
    }
}
