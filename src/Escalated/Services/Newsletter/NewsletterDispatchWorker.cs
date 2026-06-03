using Escalated.Configuration;
using Escalated.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Escalated.Services.Newsletter;

public class NewsletterDispatchWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<NewsletterDispatchWorker> _logger;
    private readonly SemaphoreSlim _overlapGate = new(1, 1);

    public NewsletterDispatchWorker(IServiceProvider services, ILogger<NewsletterDispatchWorker> logger)
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
                if (await _overlapGate.WaitAsync(0, stoppingToken))
                {
                    try
                    {
                        await TickAsync(stoppingToken);
                    }
                    finally
                    {
                        _overlapGate.Release();
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in newsletter dispatch worker.");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    public async Task TickAsync(CancellationToken ct = default)
    {
        using var scope = _services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<EscalatedOptions>>().Value;
        if (!options.EnableNewsletters)
            return;

        var db = scope.ServiceProvider.GetRequiredService<EscalatedDbContext>();
        var planner = scope.ServiceProvider.GetRequiredService<NewsletterPlanner>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<NewsletterDispatcher>();
        var now = DateTime.UtcNow;

        var due = await db.Newsletters
            .Where(n => n.Status == "scheduled" && n.ScheduledAt != null && n.ScheduledAt <= now)
            .ToListAsync(ct);

        foreach (var newsletter in due)
        {
            await planner.PlanAsync(newsletter, ct);
        }

        await dispatcher.DispatchBatchAsync(ct);
    }
}
