using Escalated.Data;
using Escalated.Enums;
using Escalated.Models;
using Escalated.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Escalated.Events;

/// <summary>
/// Bridges domain events to <see cref="IEscalatedNotificationSender"/>, as the
/// Laravel reference's <c>Send*Notification</c> listeners do: a new ticket, a
/// public reply, an assignment, a status change, an escalation, a resolution and
/// an SLA breach each call the matching sender method. Live chat tickets are
/// skipped for the ticket and reply notices; the chat itself is the conversation.
///
/// <para>The sender (the host's, or the default <see cref="EmailNotificationSender"/>)
/// is resolved from a fresh scope per event. A failure is logged and never fails
/// the change that raised the event.</para>
/// </summary>
public class NotificationEventDispatcher : IEscalatedEventDispatcher
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NotificationEventDispatcher> _logger;

    public NotificationEventDispatcher(IServiceScopeFactory scopeFactory, ILogger<NotificationEventDispatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task DispatchAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : class
    {
        if (@event is not (TicketCreatedEvent or ReplyCreatedEvent or TicketAssignedEvent or TicketStatusChangedEvent
            or TicketEscalatedEvent or TicketResolvedEvent or SlaBreachedEvent))
        {
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var sender = scope.ServiceProvider.GetRequiredService<IEscalatedNotificationSender>();
            if (sender is NullNotificationSender) return;

            switch (@event)
            {
                case TicketCreatedEvent e when !IsChat(e.Ticket):
                    await sender.SendNewTicketNotificationAsync(e.Ticket, ct);
                    break;
                case ReplyCreatedEvent e when !e.Reply.IsInternalNote:
                    var ticket = e.Reply.Ticket ?? await scope.ServiceProvider.GetRequiredService<EscalatedDbContext>()
                        .Tickets.AsNoTracking().FirstOrDefaultAsync(t => t.Id == e.Reply.TicketId, ct);
                    if (ticket is not null && !IsChat(ticket)) await sender.SendReplyNotificationAsync(e.Reply, ticket, ct);
                    break;
                case TicketAssignedEvent e:
                    await sender.SendAssignmentNotificationAsync(e.Ticket, e.AgentId, e.CauserId, ct);
                    break;
                case TicketStatusChangedEvent e when !IsChat(e.Ticket):
                    await sender.SendStatusChangeNotificationAsync(e.Ticket, e.OldStatus.ToValue(), e.NewStatus.ToValue(), e.CauserId, ct);
                    break;
                case TicketEscalatedEvent e:
                    await sender.SendEscalationNotificationAsync(e.Ticket, ct);
                    break;
                case TicketResolvedEvent e when !IsChat(e.Ticket):
                    await sender.SendResolvedNotificationAsync(e.Ticket, ct);
                    break;
                case SlaBreachedEvent e:
                    await sender.SendSlaBreachNotificationAsync(e.Ticket, e.BreachType, ct);
                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "[NotificationEventDispatcher] failed sending notifications for {Event}", typeof(TEvent).Name);
        }
    }

    private static bool IsChat(Ticket ticket) => ticket.TicketType == "chat";
}
