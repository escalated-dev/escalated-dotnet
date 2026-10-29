using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Escalated.Plugins;

/// <summary>
/// Five-field cron expressions (<c>minute hour day-of-month month day-of-week</c>), as
/// the plugin SDK's <c>cron</c> keys are written, evaluated in UTC. Supports <c>*</c>,
/// numbers, <c>a-b</c> ranges, <c>/step</c>, comma lists, month and weekday names, 7
/// as Sunday, and the <c>@hourly</c>-style shorthands. When both day fields are
/// restricted, either one matching is enough, as cron does.
/// </summary>
public static class PluginCron
{
    private static readonly Dictionary<string, string> Shorthands = new(StringComparer.OrdinalIgnoreCase)
    {
        ["@yearly"] = "0 0 1 1 *",
        ["@annually"] = "0 0 1 1 *",
        ["@monthly"] = "0 0 1 * *",
        ["@weekly"] = "0 0 * * 0",
        ["@daily"] = "0 0 * * *",
        ["@midnight"] = "0 0 * * *",
        ["@hourly"] = "0 * * * *",
    };

    private static readonly string[] Months = { "jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec" };
    private static readonly string[] Days = { "sun", "mon", "tue", "wed", "thu", "fri", "sat" };

    private sealed record Field(int Min, int Max, string[] Names, int Base);

    private static readonly Field[] Fields =
    {
        new(0, 59, Array.Empty<string>(), 0),
        new(0, 23, Array.Empty<string>(), 0),
        new(1, 31, Array.Empty<string>(), 0),
        new(1, 12, Months, 1),
        new(0, 7, Days, 0),
    };

    /// <summary>Whether <paramref name="expression"/> is due in the minute containing <paramref name="at"/> (UTC).</summary>
    /// <exception cref="FormatException">The expression cannot be read.</exception>
    public static bool Due(string expression, DateTimeOffset at)
    {
        var text = expression.Trim();
        if (Shorthands.TryGetValue(text, out var shorthand)) text = shorthand;
        var parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5) throw Invalid(expression);

        var values = new HashSet<int>[5];
        var any = new bool[5];
        for (var i = 0; i < 5; i++) (values[i], any[i]) = Parse(parts[i], Fields[i], expression);
        if (values[4].Contains(7)) values[4].Add(0);

        var utc = at.UtcDateTime;
        if (!values[0].Contains(utc.Minute) || !values[1].Contains(utc.Hour) || !values[3].Contains(utc.Month)) return false;
        var dom = values[2].Contains(utc.Day);
        var dow = values[4].Contains((int)utc.DayOfWeek);
        return !any[2] && !any[4] ? dom || dow : dom && dow;
    }

    private static (HashSet<int>, bool) Parse(string text, Field field, string expression)
    {
        var values = new HashSet<int>();
        var any = false;
        foreach (var part in text.Split(','))
        {
            var pieces = part.Split('/');
            if (pieces.Length > 2) throw Invalid(expression);
            var step = pieces.Length == 2 ? Number(pieces[1], 1, int.MaxValue, expression) : 1;
            int from, to;
            var range = pieces[0];
            if (range == "*")
            {
                (from, to) = (field.Min, field.Max);
                any = pieces.Length == 1;
            }
            else if (range.Contains('-'))
            {
                var ends = range.Split('-');
                if (ends.Length != 2) throw Invalid(expression);
                (from, to) = (Value(ends[0], field, expression), Value(ends[1], field, expression));
                if (from > to) throw Invalid(expression);
            }
            else
            {
                from = Value(range, field, expression);
                to = pieces.Length == 2 ? field.Max : from;
            }

            for (var n = from; n <= to; n += step) values.Add(n);
        }

        return (values, any);
    }

    private static int Value(string token, Field field, string expression)
    {
        var name = Array.FindIndex(field.Names, n => string.Equals(n, token, StringComparison.OrdinalIgnoreCase));
        return name >= 0 ? name + field.Base : Number(token, field.Min, field.Max, expression);
    }

    private static int Number(string token, int min, int max, string expression)
    {
        if (token.Length == 0 || token.Length > 9 || !token.All(char.IsAsciiDigit)) throw Invalid(expression);
        var n = int.Parse(token, System.Globalization.CultureInfo.InvariantCulture);
        if (n < min || n > max) throw Invalid(expression);
        return n;
    }

    private static FormatException Invalid(string expression) => new($"Invalid cron expression \"{expression}\".");
}

/// <summary>
/// Runs active plugins' due cron schedules (SDK and native) at the start of every minute (UTC),
/// through <see cref="PluginRegistry.RunDueCronAsync"/>.
/// </summary>
public class PluginCronBackgroundService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<PluginCronBackgroundService> _logger;
    private readonly TimeProvider _time;

    public PluginCronBackgroundService(IServiceProvider services, ILogger<PluginCronBackgroundService> logger, TimeProvider? time = null)
    {
        _services = services;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_services.GetRequiredService<Bridge.PluginBridge>().IsEnabled
            && !_services.GetServices<IEscalatedPlugin>().Any(p => p is IPluginCron))
        {
            return;
        }
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = _time.GetUtcNow();
            var next = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, TimeSpan.Zero).AddMinutes(1);
            await Task.Delay(next - now, _time, stoppingToken);
            try
            {
                await RunOnceAsync(next, stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(e, "[Escalated plugins] running plugin cron schedules failed");
            }
        }
    }

    /// <summary>
    /// One minute's run: the due schedules side by side, each in its own scope (a
    /// plugin's ctx calls use that scope's database context).
    /// </summary>
    public async Task RunOnceAsync(DateTimeOffset minute, CancellationToken ct)
    {
        IReadOnlyList<(string Plugin, string Schedule)> due;
        using (var scope = _services.CreateScope())
        {
            due = await scope.ServiceProvider.GetRequiredService<PluginRegistry>().DueCronAsync(minute, ct);
        }

        await Task.WhenAll(due.Select(async run =>
        {
            using var scope = _services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<PluginRegistry>().RunCronAsync(run.Plugin, run.Schedule, ct);
        }));
    }
}
