using Escalated.Enums;
using Escalated.Models;

namespace Escalated.Events;

public record TicketCreatedEvent(Ticket Ticket);
public record TicketUpdatedEvent(Ticket Ticket);
public record TicketStatusChangedEvent(Ticket Ticket, TicketStatus OldStatus, TicketStatus NewStatus, int? CauserId);
public record TicketAssignedEvent(Ticket Ticket, int AgentId, int? CauserId);
public record TicketUnassignedEvent(Ticket Ticket, int? PreviousAgentId, int? CauserId);
public record TicketResolvedEvent(Ticket Ticket, int? CauserId);
public record TicketClosedEvent(Ticket Ticket, int? CauserId);
public record TicketReopenedEvent(Ticket Ticket, int? CauserId);
public record TicketEscalatedEvent(Ticket Ticket, int? CauserId);
public record TicketPriorityChangedEvent(Ticket Ticket, TicketPriority OldPriority, TicketPriority NewPriority, int? CauserId);
public record DepartmentChangedEvent(Ticket Ticket, int? OldDepartmentId, int NewDepartmentId, int? CauserId);
public record ReplyCreatedEvent(Reply Reply);
public record InternalNoteAddedEvent(Reply Reply);
public record SlaBreachedEvent(Ticket Ticket, string BreachType);
public record SlaWarningEvent(Ticket Ticket, string WarningType, int MinutesRemaining);
public record TagAddedEvent(Ticket Ticket, int TagId, int? CauserId);
public record TagRemovedEvent(Ticket Ticket, int TagId, int? CauserId);

/// <summary>
/// Central event dispatcher interface for the Escalated system.
/// Host applications implement this to receive domain events.
/// </summary>
public interface IEscalatedEventDispatcher
{
    Task DispatchAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : class;
}

/// <summary>
/// Default no-op implementation. Host apps override by registering their own IEscalatedEventDispatcher.
/// </summary>
public class NullEventDispatcher : IEscalatedEventDispatcher
{
    public Task DispatchAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : class
        => Task.CompletedTask;
}
