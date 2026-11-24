using System.Text.Json.Serialization;
using Escalated.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Escalated.Services;

/// <summary>One recurring job and how often it runs.</summary>
public sealed record ScheduledJob(
    [property: JsonPropertyName("command")] string Command,
    [property: JsonPropertyName("interval")] string Interval);

/// <summary>
/// Whether the recurring jobs are running, as the admin pages (<c>scheduler</c> prop) and
/// <c>GET support/admin/settings/scheduler</c> send it: <c>healthy</c> is false when the SLA check
/// has never run or last ran more than 15 minutes ago.
/// </summary>
public sealed record SchedulerStatus(
    [property: JsonPropertyName("healthy")] bool Healthy,
    [property: JsonPropertyName("last_run_at")] string? LastRunAt,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("commands")] IReadOnlyList<ScheduledJob> Commands);

/// <summary>
/// The SLA check's heartbeat. <see cref="SlaMonitorBackgroundService"/> records the time
/// of every completed run in the settings table (one row, shared by every instance);
/// <see cref="StatusAsync"/> reads it back. When the heartbeat is missing or older than
/// <see cref="StaleAfter"/> the recurring jobs are not running, and the status says so,
/// names each job, and logs a warning (at most once every 15 minutes per process).
/// </summary>
public class SchedulerHeartbeat
{
    /// <summary>The settings row holding the last SLA check, ISO 8601 UTC.</summary>
    public const string SettingKey = "scheduler_sla_check_at";

    /// <summary>A heartbeat older than this means the recurring jobs are not running.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(15);

    /// <summary>The recurring jobs <c>AddEscalated()</c> runs as hosted services.</summary>
    public static readonly IReadOnlyList<ScheduledJob> Jobs = new[]
    {
        new ScheduledJob(nameof(SlaMonitorBackgroundService), "every minute"),
        new ScheduledJob(nameof(EscalationBackgroundService), "every 5 minutes"),
        new ScheduledJob(nameof(AutomationBackgroundService), "every 5 minutes"),
        new ScheduledJob(nameof(WorkflowDelayedActionBackgroundService), "every minute"),
        new ScheduledJob(nameof(TicketSnoozeBackgroundService), "every minute"),
        new ScheduledJob(nameof(ChatCleanupBackgroundService), "every minute"),
        new ScheduledJob(nameof(RetentionPurgeBackgroundService), "daily"),
    };

    private static readonly object WarnLock = new();
    private static DateTimeOffset _lastWarned = DateTimeOffset.MinValue;

    private readonly SettingsService _settings;
    private readonly TimeProvider _clock;
    private readonly ILogger _logger;

    public SchedulerHeartbeat(EscalatedDbContext db, TimeProvider? clock = null, ILogger<SchedulerHeartbeat>? logger = null)
    {
        _settings = new SettingsService(db);
        _clock = clock ?? TimeProvider.System;
        _logger = logger ?? (ILogger)NullLogger.Instance;
    }

    /// <summary>Records that the SLA check ran now.</summary>
    public Task RecordAsync(CancellationToken ct = default) =>
        _settings.SetAsync(SettingKey, _clock.GetUtcNow().UtcDateTime.ToString("O"), ct);

    /// <summary>When the SLA check last ran, or null when it never has.</summary>
    public async Task<DateTimeOffset?> LastRunAsync(CancellationToken ct = default)
    {
        var value = await _settings.GetAsync(SettingKey, ct: ct);
        return DateTimeOffset.TryParse(value, null, System.Globalization.DateTimeStyles.AssumeUniversal, out var at)
            ? at.ToUniversalTime()
            : null;
    }

    public async Task<SchedulerStatus> StatusAsync(CancellationToken ct = default)
    {
        var lastRun = await LastRunAsync(ct);
        var now = _clock.GetUtcNow();
        var healthy = lastRun is { } at && now - at <= StaleAfter;
        var lastRunAt = lastRun?.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

        if (healthy)
        {
            return new SchedulerStatus(true, lastRunAt, "The recurring jobs are running.", Jobs);
        }

        var when = lastRun is null ? "has never run" : $"last ran at {lastRunAt}";
        var message =
            $"Escalated's recurring jobs are not running: the SLA check {when}, so SLA breaches, escalation rules, " +
            "automations, delayed workflow actions and snooze wake-ups are not processed. AddEscalated() runs them as " +
            "hosted services in your app: keep them registered and keep the app process running (on IIS, turn off the " +
            "application pool's idle time-out). If you run them elsewhere, call each service's RunOnceAsync on the " +
            "interval listed: " + string.Join(", ", Jobs.Select(j => $"{j.Command} {j.Interval}")) + ".";

        WarnOnce(now, message);
        return new SchedulerStatus(false, lastRunAt, message, Jobs);
    }

    private void WarnOnce(DateTimeOffset now, string message)
    {
        lock (WarnLock)
        {
            if ((now - _lastWarned).Duration() < StaleAfter)
            {
                return;
            }

            _lastWarned = now;
        }

        _logger.LogWarning("{Message}", message);
    }
}

/// <summary>
/// The <c>escalated-scheduler</c> health check: Degraded, with the status as data, while the
/// recurring jobs are not running. Registered by <c>AddEscalated()</c>; a host that maps
/// <c>app.MapHealthChecks("/health")</c> gets it.
/// </summary>
public sealed class EscalatedSchedulerHealthCheck : IHealthCheck
{
    public const string Name = "escalated-scheduler";

    private readonly IServiceScopeFactory _scopes;

    public EscalatedSchedulerHealthCheck(IServiceScopeFactory scopes)
    {
        _scopes = scopes;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var scope = _scopes.CreateScope();
        var status = await scope.ServiceProvider.GetRequiredService<SchedulerHeartbeat>().StatusAsync(cancellationToken);
        var data = new Dictionary<string, object>
        {
            ["healthy"] = status.Healthy,
            ["last_run_at"] = (object?)status.LastRunAt ?? string.Empty,
            ["commands"] = status.Commands,
        };

        return status.Healthy
            ? HealthCheckResult.Healthy(status.Message, data)
            : HealthCheckResult.Degraded(status.Message, data: data);
    }
}
