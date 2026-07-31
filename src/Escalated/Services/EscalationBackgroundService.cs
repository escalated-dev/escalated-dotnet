using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Escalated.Services;

/// <summary>
/// Background service that periodically evaluates escalation rules against
/// open tickets via <see cref="EscalationService"/>, escalating / reassigning
/// / re-prioritising tickets that match.
///
/// Escalation rules are time-based (age / no-response thresholds) and so need
/// a hosted evaluator to fire at all. Mirrors the loop shape of
/// <see cref="TicketSnoozeBackgroundService"/>.
/// </summary>
public class EscalationBackgroundService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<EscalationBackgroundService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(5);

    public EscalationBackgroundService(IServiceProvider services, ILogger<EscalationBackgroundService> logger)
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
                _logger.LogError(ex, "Error in escalation background service");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    /// <summary>
    /// Runs a single escalation-rule sweep in its own DI scope. Exposed so the
    /// wiring (resolve <see cref="EscalationService"/> and invoke it) can be
    /// verified without spinning the timer loop.
    /// </summary>
    public async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var escalation = scope.ServiceProvider.GetRequiredService<EscalationService>();
        var escalated = await escalation.EvaluateRulesAsync(ct);

        if (escalated > 0)
            _logger.LogInformation("Escalation rules escalated {Count} tickets", escalated);
    }
}
