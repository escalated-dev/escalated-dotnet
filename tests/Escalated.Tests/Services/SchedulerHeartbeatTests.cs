using System.Net.Http.Json;
using System.Text.Json;
using Escalated.Configuration;
using Escalated.Data;
using Escalated.Events;
using Escalated.Services;
using Escalated.Tests.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using static Escalated.Tests.Hosting.InertiaTestSupport;

namespace Escalated.Tests.Services;

/// <summary>
/// The SLA check's heartbeat and the warning admins get when the recurring jobs stop
/// running: the <c>scheduler</c> prop on the admin Settings and Reports pages, the
/// <c>GET support/admin/settings/scheduler</c> JSON and the <c>escalated-scheduler</c> health check.
/// </summary>
public class SchedulerHeartbeatTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static EscalatedDbContext Db() => new(new DbContextOptionsBuilder<EscalatedDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options);

    [Fact]
    public async Task NoHeartbeat_IsUnhealthyAndNamesWhatToRun()
    {
        await using var db = Db();
        var status = await new SchedulerHeartbeat(db, new Clock()).StatusAsync();

        Assert.False(status.Healthy);
        Assert.Null(status.LastRunAt);
        Assert.Contains("not running", status.Message);
        Assert.Contains("has never run", status.Message);
        Assert.Contains(nameof(SlaMonitorBackgroundService), status.Message);
        Assert.Contains(status.Commands, c => c.Command == nameof(SlaMonitorBackgroundService) && c.Interval == "every minute");
        Assert.Contains(status.Commands, c => c.Command == nameof(EscalationBackgroundService));
        Assert.Contains(status.Commands, c => c.Command == nameof(AutomationBackgroundService));
        Assert.Contains(status.Commands, c => c.Command == nameof(TicketSnoozeBackgroundService));
    }

    [Fact]
    public async Task ARecentHeartbeat_IsHealthy_AndOneOlderThanFifteenMinutesIsNot()
    {
        await using var db = Db();
        var clock = new Clock();
        var heartbeat = new SchedulerHeartbeat(db, clock);

        await heartbeat.RecordAsync();
        clock.Now = clock.Now.AddMinutes(15);
        var fresh = await heartbeat.StatusAsync();
        Assert.True(fresh.Healthy);
        Assert.Equal("2026-11-24T12:00:00.000Z", fresh.LastRunAt);

        clock.Now = clock.Now.AddMinutes(1);
        var stale = await heartbeat.StatusAsync();
        Assert.False(stale.Healthy);
        Assert.Equal("2026-11-24T12:00:00.000Z", stale.LastRunAt);
        Assert.Contains("last ran at 2026-11-24T12:00:00.000Z", stale.Message);
    }

    [Fact]
    public async Task SlaMonitorRun_RecordsTheHeartbeat()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IOptions<EscalatedOptions>>(Options.Create(new EscalatedOptions()));
        var name = Guid.NewGuid().ToString();
        services.AddDbContext<EscalatedDbContext>(o => o
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        services.AddSingleton<IEscalatedEventDispatcher, NullEventDispatcher>();
        services.AddScoped<SlaService>();
        await using var provider = services.BuildServiceProvider();

        await new SlaMonitorBackgroundService(provider, NullLogger<SlaMonitorBackgroundService>.Instance)
            .RunOnceAsync(CancellationToken.None);

        using var scope = provider.CreateScope();
        var status = await ActivatorUtilities.CreateInstance<SchedulerHeartbeat>(scope.ServiceProvider).StatusAsync();
        Assert.True(status.Healthy);
        Assert.NotNull(status.LastRunAt);
    }

    [Fact]
    public async Task AdminPagesAndJson_CarryTheSchedulerStatus()
    {
        await using var host = await EscalatedTestHost.StartAsync(options => options.Ui.Enabled = true);
        await host.GrantRoleAsync("admin-1", "escalated-admin");
        var client = host.ClientFor("admin-1");

        foreach (var url in new[] { "/support/admin/settings", "/support/admin/reports", "/support/admin/reports/dashboard" })
        {
            var scheduler = (await PageAsync(client, url)).GetProperty("props").GetProperty("scheduler");
            Assert.False(scheduler.GetProperty("healthy").GetBoolean());
            Assert.Equal(JsonValueKind.Null, scheduler.GetProperty("last_run_at").ValueKind);
            Assert.Contains("not running", scheduler.GetProperty("message").GetString());
            var first = scheduler.GetProperty("commands")[0];
            Assert.Equal(nameof(SlaMonitorBackgroundService), first.GetProperty("command").GetString());
            Assert.Equal("every minute", first.GetProperty("interval").GetString());
        }

        using (var scope = host.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<SchedulerHeartbeat>().RecordAsync();
        }

        var page = await PageAsync(client, "/support/admin/settings");
        Assert.True(page.GetProperty("props").GetProperty("scheduler").GetProperty("healthy").GetBoolean());

        var json = await client.GetFromJsonAsync<JsonElement>("/support/admin/settings/scheduler");
        Assert.True(json.GetProperty("healthy").GetBoolean());
        Assert.EndsWith("Z", json.GetProperty("last_run_at").GetString());

        // The heartbeat row is internal, not a setting.
        var settings = await client.GetFromJsonAsync<JsonElement>("/support/admin/settings");
        Assert.False(settings.TryGetProperty(SchedulerHeartbeat.SettingKey, out _));
    }

    [Fact]
    public async Task HealthCheck_IsDegradedWithoutAHeartbeat()
    {
        await using var host = await EscalatedTestHost.StartAsync();
        var health = host.Services.GetRequiredService<HealthCheckService>();

        var report = await health.CheckHealthAsync(r => r.Name == EscalatedSchedulerHealthCheck.Name);
        Assert.Equal(HealthStatus.Degraded, report.Entries[EscalatedSchedulerHealthCheck.Name].Status);

        using (var scope = host.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<SchedulerHeartbeat>().RecordAsync();
        }

        report = await health.CheckHealthAsync(r => r.Name == EscalatedSchedulerHealthCheck.Name);
        Assert.Equal(HealthStatus.Healthy, report.Entries[EscalatedSchedulerHealthCheck.Name].Status);
    }
}
