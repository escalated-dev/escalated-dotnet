using Escalated.Configuration;
using Escalated.Data;
using Escalated.Enums;
using Escalated.Events;
using Escalated.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Escalated.Services;

public class SlaService
{
    private readonly EscalatedDbContext _db;
    private readonly IEscalatedEventDispatcher _events;
    private readonly EscalatedOptions _options;

    public SlaService(EscalatedDbContext db, IEscalatedEventDispatcher events, IOptions<EscalatedOptions> options)
    {
        _db = db;
        _events = events;
        _options = options.Value;
    }

    /// <summary>
    /// Attach the default SLA policy to a ticket.
    /// </summary>
    public async Task AttachDefaultPolicyAsync(Ticket ticket, CancellationToken ct = default)
    {
        var policy = await _db.SlaPolicies
            .Where(p => p.IsActive && p.IsDefault)
            .FirstOrDefaultAsync(ct);

        if (policy == null) return;
        await AttachPolicyAsync(ticket, policy, ct);
    }

    /// <summary>
    /// Attach a specific SLA policy and calculate due dates.
    /// </summary>
    public async Task AttachPolicyAsync(Ticket ticket, SlaPolicy policy, CancellationToken ct = default)
    {
        ticket.SlaPolicyId = policy.Id;

        var firstResponseHours = policy.GetFirstResponseHoursFor(ticket.Priority);
        var resolutionHours = policy.GetResolutionHoursFor(ticket.Priority);

        if (firstResponseHours.HasValue)
        {
            ticket.FirstResponseDueAt = CalculateDueDate(
                ticket.CreatedAt, firstResponseHours.Value, policy.BusinessHoursOnly);
        }

        if (resolutionHours.HasValue)
        {
            ticket.ResolutionDueAt = CalculateDueDate(
                ticket.CreatedAt, resolutionHours.Value, policy.BusinessHoursOnly);
        }

        _db.Tickets.Update(ticket);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Check all open tickets for SLA breaches and flag them. Returns count of newly breached tickets.
    /// </summary>
    public async Task<int> CheckBreachesAsync(CancellationToken ct = default)
    {
        var breached = 0;
        var now = DateTime.UtcNow;

        // First response breaches
        var frTickets = await _db.Tickets
            .Where(t => t.Status != TicketStatus.Resolved && t.Status != TicketStatus.Closed)
            .Where(t => t.FirstResponseDueAt != null)
            .Where(t => t.FirstResponseAt == null)
            .Where(t => !t.SlaFirstResponseBreached)
            .Where(t => t.FirstResponseDueAt < now)
            .ToListAsync(ct);

        foreach (var ticket in frTickets)
        {
            ticket.SlaFirstResponseBreached = true;
            ticket.UpdatedAt = now;
            _db.Tickets.Update(ticket);
            await _events.DispatchAsync(new SlaBreachedEvent(ticket, "first_response"), ct);
            breached++;
        }

        // Resolution breaches
        var resTickets = await _db.Tickets
            .Where(t => t.Status != TicketStatus.Resolved && t.Status != TicketStatus.Closed)
            .Where(t => t.ResolutionDueAt != null)
            .Where(t => !t.SlaResolutionBreached)
            .Where(t => t.ResolutionDueAt < now)
            .ToListAsync(ct);

        foreach (var ticket in resTickets)
        {
            ticket.SlaResolutionBreached = true;
            ticket.UpdatedAt = now;
            _db.Tickets.Update(ticket);
            await _events.DispatchAsync(new SlaBreachedEvent(ticket, "resolution"), ct);
            breached++;
        }

        if (breached > 0)
            await _db.SaveChangesAsync(ct);

        return breached;
    }

    /// <summary>
    /// Check for tickets approaching their SLA deadlines. Returns count of warnings issued.
    /// </summary>
    public async Task<int> CheckWarningsAsync(int warningMinutes = 30, CancellationToken ct = default)
    {
        var warned = 0;
        var now = DateTime.UtcNow;
        var threshold = now.AddMinutes(warningMinutes);

        var frTickets = await _db.Tickets
            .Where(t => t.Status != TicketStatus.Resolved && t.Status != TicketStatus.Closed)
            .Where(t => t.FirstResponseDueAt != null)
            .Where(t => t.FirstResponseAt == null)
            .Where(t => !t.SlaFirstResponseBreached)
            .Where(t => t.FirstResponseDueAt >= now && t.FirstResponseDueAt <= threshold)
            .ToListAsync(ct);

        foreach (var ticket in frTickets)
        {
            var minutes = (int)(ticket.FirstResponseDueAt!.Value - now).TotalMinutes;
            await _events.DispatchAsync(new SlaWarningEvent(ticket, "first_response", minutes), ct);
            warned++;
        }

        var resTickets = await _db.Tickets
            .Where(t => t.Status != TicketStatus.Resolved && t.Status != TicketStatus.Closed)
            .Where(t => t.ResolutionDueAt != null)
            .Where(t => !t.SlaResolutionBreached)
            .Where(t => t.ResolutionDueAt >= now && t.ResolutionDueAt <= threshold)
            .ToListAsync(ct);

        foreach (var ticket in resTickets)
        {
            var minutes = (int)(ticket.ResolutionDueAt!.Value - now).TotalMinutes;
            await _events.DispatchAsync(new SlaWarningEvent(ticket, "resolution", minutes), ct);
            warned++;
        }

        return warned;
    }

    /// <summary>
    /// Record first response time when an agent replies.
    /// </summary>
    public async Task RecordFirstResponseAsync(Ticket ticket, CancellationToken ct = default)
    {
        if (ticket.FirstResponseAt != null) return;

        ticket.FirstResponseAt = DateTime.UtcNow;
        ticket.UpdatedAt = DateTime.UtcNow;
        _db.Tickets.Update(ticket);
        await _db.SaveChangesAsync(ct);
    }

    private DateTime CalculateDueDate(DateTime from, double hours, bool businessHoursOnly)
    {
        if (!businessHoursOnly)
            return from.AddHours(hours);

        var config = _options.Sla.BusinessHours;
        var startParts = config.Start.Split(':');
        var endParts = config.End.Split(':');
        var startHour = int.Parse(startParts[0]);
        var startMin = int.Parse(startParts[1]);
        var endHour = int.Parse(endParts[0]);
        var endMin = int.Parse(endParts[1]);
        var days = config.Days;

        var tz = TimeZoneInfo.FindSystemTimeZoneById(config.Timezone);
        var current = TimeZoneInfo.ConvertTimeFromUtc(from, tz);
        var remainingMinutes = hours * 60;

        while (remainingMinutes > 0)
        {
            var dotNetDow = (int)current.DayOfWeek;
            // Convert .NET DayOfWeek (Sunday=0) to ISO (Monday=1..Sunday=7)
            var isoDow = dotNetDow == 0 ? 7 : dotNetDow;

            if (days.Contains(isoDow))
            {
                var dayStart = new DateTime(current.Year, current.Month, current.Day, startHour, startMin, 0);
                var dayEnd = new DateTime(current.Year, current.Month, current.Day, endHour, endMin, 0);

                if (current < dayStart)
                    current = dayStart;

                if (current < dayEnd)
                {
                    var availableMinutes = (dayEnd - current).TotalMinutes;
                    if (availableMinutes >= remainingMinutes)
                    {
                        current = current.AddMinutes(remainingMinutes);
                        remainingMinutes = 0;
                        break;
                    }
                    remainingMinutes -= availableMinutes;
                }
            }

            current = current.AddDays(1).Date.AddHours(startHour).AddMinutes(startMin);
        }

        return TimeZoneInfo.ConvertTimeToUtc(current, tz);
    }
}
