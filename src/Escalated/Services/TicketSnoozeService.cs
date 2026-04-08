using Escalated.Data;
using Escalated.Enums;
using Escalated.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Escalated.Services;

public class TicketSnoozeService
{
    private readonly EscalatedDbContext _db;
    private readonly TicketService _ticketService;

    public TicketSnoozeService(EscalatedDbContext db, TicketService ticketService)
    {
        _db = db;
        _ticketService = ticketService;
    }

    /// <summary>
    /// Snooze a ticket until a specified date. Sets status and records activity.
    /// </summary>
    public async Task<Ticket> SnoozeAsync(Ticket ticket, DateTime snoozeUntil, int? causerId = null,
        CancellationToken ct = default)
    {
        ticket.SnoozedUntil = snoozeUntil;
        ticket.UpdatedAt = DateTime.UtcNow;
        _db.Tickets.Update(ticket);

        await _ticketService.LogActivityAsync(ticket, ActivityType.Snoozed, causerId,
            new Dictionary<string, object>
            {
                ["snoozed_until"] = snoozeUntil.ToString("O")
            }, ct);

        await _db.SaveChangesAsync(ct);
        return ticket;
    }

    /// <summary>
    /// Unsnooze a ticket and reopen it.
    /// </summary>
    public async Task<Ticket> UnsnoozeAsync(Ticket ticket, int? causerId = null,
        CancellationToken ct = default)
    {
        ticket.SnoozedUntil = null;
        ticket.UpdatedAt = DateTime.UtcNow;
        _db.Tickets.Update(ticket);

        await _ticketService.LogActivityAsync(ticket, ActivityType.Unsnoozed, causerId, null, ct);

        await _db.SaveChangesAsync(ct);

        // Reopen if it was snoozed
        if (ticket.Status == TicketStatus.Resolved || ticket.Status == TicketStatus.Closed)
        {
            ticket = await _ticketService.ChangeStatusAsync(ticket, TicketStatus.Reopened, causerId, ct);
        }

        return ticket;
    }

    /// <summary>
    /// Wake up all tickets whose snooze period has expired.
    /// </summary>
    public async Task<int> WakeExpiredAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var snoozedTickets = await _db.Tickets
            .Where(t => t.SnoozedUntil != null && t.SnoozedUntil <= now)
            .Where(t => t.DeletedAt == null)
            .ToListAsync(ct);

        foreach (var ticket in snoozedTickets)
        {
            await UnsnoozeAsync(ticket, null, ct);
        }

        return snoozedTickets.Count;
    }
}

/// <summary>
/// Background service that periodically wakes snoozed tickets.
/// </summary>
public class TicketSnoozeBackgroundService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<TicketSnoozeBackgroundService> _logger;

    public TicketSnoozeBackgroundService(IServiceProvider services, ILogger<TicketSnoozeBackgroundService> logger)
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
                var snoozeService = scope.ServiceProvider.GetRequiredService<TicketSnoozeService>();
                var woken = await snoozeService.WakeExpiredAsync(stoppingToken);

                if (woken > 0)
                    _logger.LogInformation("Unsnoozed {Count} tickets", woken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in snooze background service");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
