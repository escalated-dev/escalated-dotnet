using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Escalated.Services;

/// <summary>
/// Background service that periodically runs time-based Automations against
/// open tickets via <see cref="AutomationRunner"/>.
///
/// Automations are the time-driven counterpart to (event-driven) Workflows —
/// they scan <c>hours_since_*</c> style conditions on a schedule — so without
/// a hosted runner they never fire. Mirrors the loop shape of
/// <see cref="TicketSnoozeBackgroundService"/>.
/// </summary>
public class AutomationBackgroundService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<AutomationBackgroundService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(5);

    public AutomationBackgroundService(IServiceProvider services, ILogger<AutomationBackgroundService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in automation background service");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    /// <summary>
    /// Runs a single automation sweep in its own DI scope. Exposed so the
    /// wiring (resolve <see cref="AutomationRunner"/> and invoke it) can be
    /// verified without spinning the timer loop.
    /// </summary>
    public async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<AutomationRunner>();
        var affected = await runner.RunAsync(ct);

        if (affected > 0)
            _logger.LogInformation("Automations affected {Count} tickets", affected);
    }
}
