using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Escalated.Services;

/// <summary>
/// Runs <see cref="DataRetentionService.PurgeExpiredAsync"/> once a day, as
/// escalated-laravel schedules <c>escalated:purge-expired</c>. Every retention
/// period defaults to <c>never</c>, so until an admin sets one on the Data
/// retention page the only thing it removes is expired guest verification codes.
/// </summary>
public class RetentionPurgeBackgroundService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<RetentionPurgeBackgroundService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromDays(1);

    public RetentionPurgeBackgroundService(IServiceProvider services, ILogger<RetentionPurgeBackgroundService> logger)
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
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error in data retention purge");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    /// <summary>Runs one purge in its own DI scope.</summary>
    public async Task<RetentionPurgeReport> RunOnceAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var report = await scope.ServiceProvider.GetRequiredService<DataRetentionService>().PurgeExpiredAsync(ct: ct);

        if (report.ClosedTickets + report.Attachments + report.AuditLogs + report.GraceExpiredTickets > 0)
        {
            _logger.LogInformation(
                "Data retention purged {Tickets} closed tickets, {Attachments} attachments, {AuditLogs} audit logs and {Grace} tickets past the grace period",
                report.ClosedTickets, report.Attachments, report.AuditLogs, report.GraceExpiredTickets);
        }

        return report;
    }
}
