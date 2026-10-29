using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Escalated.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Escalated.Tests.Hosting.InertiaTestSupport;

namespace Escalated.Tests.Plugins;

/// <summary>
/// A native plugin with cron schedules (<see cref="IPluginCron"/>) and widget badges
/// (<see cref="IPluginWidgetBadges"/>): the same behaviour SDK plugins get through the
/// runtime, without one.
/// </summary>
public sealed class PluginNativeCronAndBadgeTests
{
    private static readonly DateTimeOffset Nine = DateTimeOffset.Parse("2026-10-29T09:00:20Z", CultureInfo.InvariantCulture);

    private static Task<PluginHost> StartAsync() =>
        PluginHost.StartAsync(services: s => s.AddEscalatedPlugin<PulsePlugin>());

    [Fact]
    public async Task DueCronSchedulesOfAnActiveNativePluginRunOnceAsNoUser()
    {
        await using var plugins = await StartAsync();
        var cron = plugins.Host.Services.GetRequiredService<PluginCronBackgroundService>();

        // Inactive: nothing runs.
        await cron.RunOnceAsync(Nine, CancellationToken.None);
        Assert.Empty(plugins.Recorder.CallsOf(PulsePlugin.Name));

        await plugins.ActivateAsync(PulsePlugin.Slug);
        await cron.RunOnceAsync(Nine, CancellationToken.None);

        var ran = plugins.Recorder.Calls.Where(c => c.Plugin == PulsePlugin.Name && c.Call.StartsWith("cron:", StringComparison.Ordinal)).ToList();
        // "0 9 * * *" throws after recording and does not stop "* * * * *"; "30 9 * * *" is not due;
        // "not a schedule" is skipped.
        Assert.Equal(new[] { "cron:* * * * *", "cron:0 9 * * *" }, ran.Select(c => c.Call).Order());
        Assert.All(ran, c => Assert.Null(c.Data?["user"]));

        await plugins.WithRegistryAsync(async r => { await r.RunDueCronAsync(Nine.AddMinutes(1)); return 0; });
        Assert.Equal(3, plugins.Recorder.Calls.Count(c => c.Plugin == PulsePlugin.Name && c.Call.StartsWith("cron:", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task NativeWidgetBadgesAreAskedAsTheViewer()
    {
        await using var plugins = await StartAsync();
        await plugins.ActivateAsync(PulsePlugin.Slug);

        var page = await PageAsync(plugins.AdminClient, "/support/admin/plugins");
        var widgets = page.GetProperty("props").GetProperty("escalated").GetProperty("plugins").GetProperty("dashboardWidgets")
            .EnumerateArray().ToDictionary(w => w.GetProperty("component").GetString()!);

        Assert.Equal("for " + PluginHost.Admin, widgets["PulseInbox"].GetProperty("badge").GetString());
        Assert.Equal(7, widgets["PulseCount"].GetProperty("badge").GetInt32());
        Assert.Equal(JsonValueKind.Null, widgets["PulseBroken"].GetProperty("badge").ValueKind);
        Assert.Equal(JsonValueKind.Null, widgets["PulseSlow"].GetProperty("badge").ValueKind);
        Assert.Equal(JsonValueKind.Null, widgets["PulseObject"].GetProperty("badge").ValueKind);
        Assert.False(widgets["PulsePlain"].TryGetProperty("badge", out _));
    }

    [Fact]
    public async Task AnInactiveNativePluginsBadgesAreNeverAsked()
    {
        await using var plugins = await StartAsync();
        await plugins.ActivateAsync(CrmPlugin.Slug);

        await PageAsync(plugins.AdminClient, "/support/admin/plugins");

        Assert.DoesNotContain(plugins.Recorder.CallsOf(PulsePlugin.Name), c => c.StartsWith("badge:", StringComparison.Ordinal));
    }

    private static async Task<JsonElement> PageAsync(HttpClient client, string url)
    {
        var response = await client.SendAsync(Visit(HttpMethod.Get, url));
        response.EnsureSuccessStatusCode();
        return await PluginHost.JsonAsync(response);
    }
}

/// <summary>A native plugin with cron schedules and badge widgets.</summary>
public sealed class PulsePlugin : EscalatedPlugin, IPluginCron, IPluginWidgetBadges
{
    public const string Name = "acme-pulse";
    public const string Slug = "acme-pulse";

    private readonly PluginRecorder _recorder;

    public PulsePlugin(PluginRecorder recorder)
    {
        _recorder = recorder;
    }

    public override PluginManifest Manifest { get; } = new()
    {
        Name = Name,
        ActionHooks = Array.Empty<string>(),
        CronSchedules = new[] { "* * * * *", "0 9 * * *", "30 9 * * *", "not a schedule" },
        Widgets = new[]
        {
            new PluginWidget { Component = "PulsePlain", Order = 1 },
            new PluginWidget { Component = "PulseInbox", Order = 2, HasBadge = true },
            new PluginWidget { Component = "PulseCount", Order = 3, HasBadge = true },
            new PluginWidget { Component = "PulseBroken", Order = 4, HasBadge = true },
            new PluginWidget { Component = "PulseSlow", Order = 5, HasBadge = true },
            new PluginWidget { Component = "PulseObject", Order = 6, HasBadge = true },
        },
    };

    public async Task RunCronAsync(string schedule, PluginContext ctx, CancellationToken ct)
    {
        _recorder.Record(Name, "cron:" + schedule, new JsonObject { ["user"] = await ctx.CurrentUserAsync(ct) });
        if (schedule == "0 9 * * *") throw new InvalidOperationException("cron failed");
    }

    public async Task<object?> BadgeAsync(string component, PluginContext ctx, CancellationToken ct)
    {
        _recorder.Record(Name, "badge:" + component);
        switch (component)
        {
            case "PulseInbox":
                return "for " + ((await ctx.CurrentUserAsync(ct))?["id"]?.ToString() ?? "nobody");
            case "PulseCount":
                return 7;
            case "PulseSlow":
                await Task.Delay(Timeout.Infinite, ct);
                return 1;
            case "PulseObject":
                return new { count = 1 };
            default:
                throw new InvalidOperationException("badge failed");
        }
    }
}
