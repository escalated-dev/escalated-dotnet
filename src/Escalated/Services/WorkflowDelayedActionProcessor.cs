using Escalated.Data;
using Escalated.Enums;
using Escalated.Events;
using Escalated.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Escalated.Services;

/// <summary>
/// Runs the actions a Workflow's <c>delay</c> held back, once they are due.
/// Mirrors Laravel's <c>escalated:process-delayed-actions</c>: the Workflow's
/// conditions are checked again against the ticket as it is now, and the held
/// actions are cancelled instead when they no longer match, when the ticket was
/// resolved or closed in the meantime, or when the Workflow was disabled or deleted.
/// The remaining actions may contain another <c>delay</c>.
/// </summary>
public class WorkflowDelayedActionProcessor
{
    private const int BatchSize = 100;

    private readonly EscalatedDbContext _db;
    private readonly WorkflowRunnerService _runner;
    private readonly WorkflowExecutorService _executor;
    private readonly TimeProvider _time;
    private readonly ILogger<WorkflowDelayedActionProcessor> _logger;

    public WorkflowDelayedActionProcessor(
        EscalatedDbContext db,
        WorkflowRunnerService runner,
        WorkflowExecutorService executor,
        ILogger<WorkflowDelayedActionProcessor> logger,
        TimeProvider? time = null)
    {
        _db = db;
        _runner = runner;
        _executor = executor;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Runs every held action due at <paramref name="now"/> (default: the current time). Returns how many ran.</summary>
    public async Task<int> ProcessDueAsync(DateTime? now = null, CancellationToken ct = default)
    {
        var at = now ?? _time.GetUtcNow().UtcDateTime;
        var due = await _db.WorkflowDelayedActions
            .Where(d => !d.Executed && !d.Cancelled && d.ExecuteAt <= at)
            .OrderBy(d => d.ExecuteAt).ThenBy(d => d.Id)
            .Take(BatchSize)
            .ToListAsync(ct);

        var processed = 0;
        foreach (var held in due)
        {
            var workflow = await _db.Workflows.FirstOrDefaultAsync(w => w.Id == held.WorkflowId, ct);
            var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == held.TicketId, ct);

            if (workflow is null || ticket is null || !workflow.IsActive
                || ticket.Status is TicketStatus.Resolved or TicketStatus.Closed
                || !await _runner.MatchesAsync(workflow, ticket, ct))
            {
                held.Cancelled = true;
            }
            else
            {
                try
                {
                    // As when a Workflow runs from an event: its own actions do not
                    // trigger Workflows again.
                    using (WorkflowEventDispatcher.SuppressNested())
                    {
                        await _executor.ExecuteAsync(ticket, held.RemainingActions, ct, workflow.Id);
                    }

                    held.Executed = true;
                    processed++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[WorkflowDelayedActionProcessor] delayed action #{Id} failed", held.Id);
                    held.Cancelled = true;
                }
            }

            held.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        return processed;
    }
}

/// <summary>Runs <see cref="WorkflowDelayedActionProcessor"/> every minute.</summary>
public class WorkflowDelayedActionBackgroundService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<WorkflowDelayedActionBackgroundService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(1);

    public WorkflowDelayedActionBackgroundService(IServiceProvider services, ILogger<WorkflowDelayedActionBackgroundService> logger)
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
                using var scope = _services.CreateScope();
                var processed = await scope.ServiceProvider.GetRequiredService<WorkflowDelayedActionProcessor>()
                    .ProcessDueAsync(ct: stoppingToken);
                if (processed > 0)
                    _logger.LogInformation("Ran {Count} delayed Workflow actions", processed);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error in delayed Workflow action service");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }
}
