using System.Text.Json;
using Escalated.Data;
using Escalated.Enums;
using Escalated.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Escalated.Services;

public class AutomationRunner
{
    private readonly EscalatedDbContext _db;
    private readonly ILogger<AutomationRunner> _logger;

    public AutomationRunner(EscalatedDbContext db, ILogger<AutomationRunner> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Evaluate all active automations against open tickets. Returns count of affected tickets.
    /// </summary>
    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        var automations = await _db.Automations
            .Where(a => a.Active)
            .OrderBy(a => a.Position)
            .ToListAsync(ct);

        var affected = 0;

        foreach (var automation in automations)
        {
            var tickets = await FindMatchingTicketsAsync(automation, ct);

            foreach (var ticket in tickets)
            {
                await ExecuteActionsAsync(automation, ticket, ct);
                affected++;
            }

            automation.LastRunAt = DateTime.UtcNow;
            _db.Automations.Update(automation);
        }

        await _db.SaveChangesAsync(ct);
        return affected;
    }

    private async Task<List<Ticket>> FindMatchingTicketsAsync(Automation automation, CancellationToken ct)
    {
        var query = _db.Tickets
            .Where(t => t.Status != TicketStatus.Resolved && t.Status != TicketStatus.Closed);

        var conditions = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(automation.Conditions)
            ?? new List<Dictionary<string, string>>();

        foreach (var condition in conditions)
        {
            var field = condition.GetValueOrDefault("field", "");
            var op = condition.GetValueOrDefault("operator", ">");
            var value = condition.GetValueOrDefault("value", "");

            switch (field)
            {
                case "hours_since_created":
                    var createdThreshold = DateTime.UtcNow.AddHours(-int.Parse(value));
                    query = ApplyDateOperator(query, t => t.CreatedAt, op, createdThreshold);
                    break;
                case "hours_since_updated":
                    var updatedThreshold = DateTime.UtcNow.AddHours(-int.Parse(value));
                    query = ApplyDateOperator(query, t => t.UpdatedAt, op, updatedThreshold);
                    break;
                case "status":
                    query = query.Where(t => t.Status == TicketStatusExtensions.Parse(value));
                    break;
                case "priority":
                    query = query.Where(t => t.Priority == TicketPriorityExtensions.Parse(value));
                    break;
                case "assigned" when value == "unassigned":
                    query = query.Where(t => t.AssignedTo == null);
                    break;
                case "assigned":
                    query = query.Where(t => t.AssignedTo != null);
                    break;
                case "ticket_type":
                    query = query.Where(t => t.TicketType == value);
                    break;
                case "subject_contains":
                    query = query.Where(t => t.Subject.Contains(value));
                    break;
            }
        }

        return await query.ToListAsync(ct);
    }

    private static IQueryable<Ticket> ApplyDateOperator(IQueryable<Ticket> query,
        System.Linq.Expressions.Expression<Func<Ticket, DateTime>> selector,
        string op, DateTime threshold)
    {
        // "hours_since > X" means "created_at < threshold" (older)
        return op switch
        {
            ">" => query.Where(t => EF.Property<DateTime>(t, GetPropertyName(selector)) < threshold),
            ">=" => query.Where(t => EF.Property<DateTime>(t, GetPropertyName(selector)) <= threshold),
            "<" => query.Where(t => EF.Property<DateTime>(t, GetPropertyName(selector)) > threshold),
            "<=" => query.Where(t => EF.Property<DateTime>(t, GetPropertyName(selector)) >= threshold),
            _ => query.Where(t => EF.Property<DateTime>(t, GetPropertyName(selector)) < threshold),
        };
    }

    private static string GetPropertyName(System.Linq.Expressions.Expression<Func<Ticket, DateTime>> expr)
    {
        if (expr.Body is System.Linq.Expressions.MemberExpression member)
            return member.Member.Name;
        throw new ArgumentException("Expression must be a member access");
    }

    private async Task ExecuteActionsAsync(Automation automation, Ticket ticket, CancellationToken ct)
    {
        var actions = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(automation.Actions)
            ?? new List<Dictionary<string, string>>();

        foreach (var action in actions)
        {
            var type = action.GetValueOrDefault("type", "");
            var value = action.GetValueOrDefault("value", "");

            try
            {
                switch (type)
                {
                    case "change_status":
                        ticket.Status = TicketStatusExtensions.Parse(value);
                        ticket.UpdatedAt = DateTime.UtcNow;
                        _db.Tickets.Update(ticket);
                        break;
                    case "assign":
                        ticket.AssignedTo = int.Parse(value);
                        ticket.UpdatedAt = DateTime.UtcNow;
                        _db.Tickets.Update(ticket);
                        break;
                    case "add_tag":
                        var tag = await _db.Tags.FirstOrDefaultAsync(t => t.Name == value, ct);
                        if (tag != null)
                        {
                            var exists = await _db.TicketTags.AnyAsync(
                                tt => tt.TicketId == ticket.Id && tt.TagId == tag.Id, ct);
                            if (!exists)
                                _db.TicketTags.Add(new TicketTag { TicketId = ticket.Id, TagId = tag.Id });
                        }
                        break;
                    case "change_priority":
                        ticket.Priority = TicketPriorityExtensions.Parse(value);
                        ticket.UpdatedAt = DateTime.UtcNow;
                        _db.Tickets.Update(ticket);
                        break;
                    case "add_note":
                        _db.Replies.Add(new Reply
                        {
                            TicketId = ticket.Id,
                            Body = value,
                            IsInternalNote = true,
                            Type = "note",
                            Metadata = JsonSerializer.Serialize(new
                            {
                                system_note = true,
                                automation_id = automation.Id
                            }),
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        });
                        break;
                    case "set_ticket_type":
                        if (Ticket.ValidTypes.Contains(value))
                        {
                            ticket.TicketType = value;
                            ticket.UpdatedAt = DateTime.UtcNow;
                            _db.Tickets.Update(ticket);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Automation action failed: automation={AutomationId}, ticket={TicketId}, action={Action}: {Error}",
                    automation.Id, ticket.Id, type, ex.Message);
            }
        }
    }
}
