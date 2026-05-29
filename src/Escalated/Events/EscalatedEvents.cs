using Escalated.Enums;
using Escalated.Models;

namespace Escalated.Events;

public record TicketCreatedEvent(Ticket Ticket);
public record TicketUpdatedEvent(Ticket Ticket);
public record TicketStatusChangedEvent(Ticket Ticket, TicketStatus OldStatus, TicketStatus NewStatus, string? CauserId);
public record TicketAssignedEvent(Ticket Ticket, string AgentId, string? CauserId);
public record TicketUnassignedEvent(Ticket Ticket, string? PreviousAgentId, string? CauserId);
public record TicketResolvedEvent(Ticket Ticket, string? CauserId);
public record TicketClosedEvent(Ticket Ticket, string? CauserId);
public record TicketReopenedEvent(Ticket Ticket, string? CauserId);
public record TicketEscalatedEvent(Ticket Ticket, string? CauserId);
public record TicketPriorityChangedEvent(Ticket Ticket, TicketPriority OldPriority, TicketPriority NewPriority, string? CauserId);
public record DepartmentChangedEvent(Ticket Ticket, int? OldDepartmentId, int NewDepartmentId, string? CauserId);
public record ReplyCreatedEvent(Reply Reply);
public record InternalNoteAddedEvent(Reply Reply);
public record SlaBreachedEvent(Ticket Ticket, string BreachType);
public record SlaWarningEvent(Ticket Ticket, string WarningType, int MinutesRemaining);
public record TagAddedEvent(Ticket Ticket, int TagId, string? CauserId);
public record TagRemovedEvent(Ticket Ticket, int TagId, string? CauserId);
public record TicketCustomActionTriggeredEvent(Ticket Ticket, string Action, string? UserId, Dictionary<string, object>? Payload, Dictionary<string, object>? Metadata);

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
