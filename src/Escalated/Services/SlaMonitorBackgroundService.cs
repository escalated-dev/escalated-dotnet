using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Escalated.Services;

/// <summary>
/// Background service that periodically scans open tickets for SLA warnings
/// and breaches via <see cref="SlaService"/>, raising <c>sla.warning</c> /
/// <c>sla.breached</c> domain events (which in turn drive Workflows and any
/// host notifications).
///
/// Without a hosted monitor these checks never run, so breaches are silently
/// never detected. Mirrors the loop shape of
/// <see cref="TicketSnoozeBackgroundService"/>.
/// </summary>
public class SlaMonitorBackgroundService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<SlaMonitorBackgroundService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(1);

    public SlaMonitorBackgroundService(IServiceProvider services, ILogger<SlaMonitorBackgroundService> logger)
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
                _logger.LogError(ex, "Error in SLA monitor background service");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    /// <summary>
    /// Runs a single SLA sweep (warnings then breaches) in its own DI scope.
    /// Exposed so the wiring (resolve <see cref="SlaService"/> and invoke the
    /// checks) can be verified without spinning the timer loop.
    /// </summary>
    public async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var sla = scope.ServiceProvider.GetRequiredService<SlaService>();

        var warned = await sla.CheckWarningsAsync(ct: ct);
        var breached = await sla.CheckBreachesAsync(ct);

        if (warned > 0 || breached > 0)
            _logger.LogInformation("SLA monitor issued {Warned} warnings and {Breached} breaches", warned, breached);
    }
}
