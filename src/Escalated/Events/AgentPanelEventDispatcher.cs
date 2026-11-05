using Escalated.Data;
using Escalated.Models;
using Escalated.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Escalated.Events;

/// <summary>
/// Hands the domain events to <see cref="AgentPanelService"/> (ADR
/// 2026-11-05-agent-panel-data): a public reply from the customer side is customer
/// activity and notifies the assignee and followers; an assignment, an SLA warning or
/// breach and a chat transfer notify. Nobody is notified of their own action.
///
/// <para>The service is resolved from a fresh scope per event. A failure is logged and
/// never fails the change that raised the event.</para>
/// </summary>
public class AgentPanelEventDispatcher : IEscalatedEventDispatcher
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AgentPanelEventDispatcher> _logger;

    public AgentPanelEventDispatcher(IServiceScopeFactory scopeFactory, ILogger<AgentPanelEventDispatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task DispatchAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : class
    {
        if (@event is not (ReplyCreatedEvent or TicketAssignedEvent or SlaWarningEvent or SlaBreachedEvent or ChatSessionTransferredEvent))
        {
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var panel = scope.ServiceProvider.GetRequiredService<AgentPanelService>();
            var db = scope.ServiceProvider.GetRequiredService<EscalatedDbContext>();

            switch (@event)
            {
                case ReplyCreatedEvent e:
                    var ticket = await db.Tickets.AsNoTracking().IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == e.Reply.TicketId, ct);
                    if (ticket is null || !AgentPanelService.CustomerSide(ticket, e.Reply)) break;
                    await panel.CustomerActivityAsync(ticket.Id, e.Reply.CreatedAt, ct);
                    var recipients = new List<string>();
                    if (ticket.AssignedTo is not null) recipients.Add(ticket.AssignedTo);
                    recipients.AddRange(await db.TicketFollowers.AsNoTracking().IgnoreQueryFilters()
                        .Where(f => f.TicketId == ticket.Id).Select(f => f.UserId).ToListAsync(ct));
                    foreach (var userId in recipients.Distinct())
                    {
                        await panel.NotifyAsync(userId, "ticket_reply", ticket, new Dictionary<string, object?> { ["reply_id"] = e.Reply.Id }, e.Reply.AuthorId, ct);
                    }

                    break;
                case TicketAssignedEvent e:
                    await panel.NotifyAsync(e.AgentId, "ticket_assigned", e.Ticket, null, e.CauserId, ct);
                    break;
                case SlaWarningEvent e:
                    await panel.NotifyAsync(e.Ticket.AssignedTo, "sla_warning", e.Ticket, new Dictionary<string, object?> { ["sla_type"] = e.WarningType }, null, ct);
                    break;
                case SlaBreachedEvent e:
                    await panel.NotifyAsync(e.Ticket.AssignedTo, "sla_breached", e.Ticket, new Dictionary<string, object?> { ["sla_type"] = e.BreachType }, null, ct);
                    break;
                case ChatSessionTransferredEvent e when e.ToAgentId is not null:
                    var chatTicket = await db.Tickets.AsNoTracking().IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == e.Session.TicketId, ct);
                    if (chatTicket is not null)
                    {
                        await panel.NotifyAsync(e.ToAgentId, "chat_transferred", chatTicket,
                            new Dictionary<string, object?> { ["session_id"] = e.Session.Id }, e.FromAgentId, ct);
                    }

                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "[AgentPanelEventDispatcher] failed handling {Event}", typeof(TEvent).Name);
        }
    }
}
