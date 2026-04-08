using System.Text.Json;
using Escalated.Data;
using Escalated.Enums;
using Escalated.Events;
using Escalated.Models;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Services;

public class EscalationService
{
    private readonly EscalatedDbContext _db;
    private readonly TicketService _ticketService;
    private readonly AssignmentService _assignmentService;
    private readonly IEscalatedEventDispatcher _events;

    public EscalationService(EscalatedDbContext db, TicketService ticketService,
        AssignmentService assignmentService, IEscalatedEventDispatcher events)
    {
        _db = db;
        _ticketService = ticketService;
        _assignmentService = assignmentService;
        _events = events;
    }

    /// <summary>
    /// Evaluate all active escalation rules against open tickets. Returns count of escalated tickets.
    /// </summary>
    public async Task<int> EvaluateRulesAsync(CancellationToken ct = default)
    {
        var rules = await _db.EscalationRules
            .Where(r => r.IsActive)
            .ToListAsync(ct);

        var escalated = 0;

        foreach (var rule in rules)
        {
            var tickets = await FindMatchingTicketsAsync(rule, ct);
            foreach (var ticket in tickets)
            {
                await ExecuteActionsAsync(ticket, rule, ct);
                escalated++;
            }
        }

        return escalated;
    }

    private async Task<List<Ticket>> FindMatchingTicketsAsync(EscalationRule rule, CancellationToken ct)
    {
        var query = _db.Tickets
            .Where(t => t.Status != TicketStatus.Resolved && t.Status != TicketStatus.Closed);

        var conditions = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(rule.Conditions)
            ?? new List<Dictionary<string, string>>();

        foreach (var condition in conditions)
        {
            var field = condition.GetValueOrDefault("field", "");
            var value = condition.GetValueOrDefault("value", "");

            query = field switch
            {
                "status" => query.Where(t => t.Status == TicketStatusExtensions.Parse(value)),
                "priority" => query.Where(t => t.Priority == TicketPriorityExtensions.Parse(value)),
                "assigned" when value == "unassigned" => query.Where(t => t.AssignedTo == null),
                "assigned" => query.Where(t => t.AssignedTo != null),
                "age_hours" => query.Where(t => t.CreatedAt <= DateTime.UtcNow.AddHours(-int.Parse(value))),
                "no_response_hours" => query.Where(t =>
                    t.FirstResponseAt == null &&
                    t.CreatedAt <= DateTime.UtcNow.AddHours(-int.Parse(value))),
                "sla_breached" => query.Where(t => t.SlaFirstResponseBreached || t.SlaResolutionBreached),
                "department_id" => query.Where(t => t.DepartmentId == int.Parse(value)),
                _ => query
            };
        }

        return await query.ToListAsync(ct);
    }

    private async Task ExecuteActionsAsync(Ticket ticket, EscalationRule rule, CancellationToken ct)
    {
        var actions = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(rule.Actions)
            ?? new List<Dictionary<string, string>>();

        var didEscalate = false;

        foreach (var action in actions)
        {
            var type = action.GetValueOrDefault("type", "");
            var value = action.GetValueOrDefault("value", "");

            switch (type)
            {
                case "escalate":
                    await _ticketService.ChangeStatusAsync(ticket, TicketStatus.Escalated, null, ct);
                    didEscalate = true;
                    break;
                case "change_priority":
                    await _ticketService.ChangePriorityAsync(ticket, TicketPriorityExtensions.Parse(value), null, ct);
                    break;
                case "assign_to":
                    await _assignmentService.AssignAsync(ticket, int.Parse(value), null, ct);
                    break;
                case "change_department":
                    await _ticketService.ChangeDepartmentAsync(ticket, int.Parse(value), null, ct);
                    break;
            }
        }

        if (didEscalate)
        {
            await _events.DispatchAsync(new TicketEscalatedEvent(ticket, null), ct);
        }
    }
}
