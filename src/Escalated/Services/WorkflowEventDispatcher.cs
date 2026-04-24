using Escalated.Events;
using Escalated.Models;
using Microsoft.Extensions.Logging;

namespace Escalated.Services;

/// <summary>
/// Decorator implementation of <see cref="IEscalatedEventDispatcher"/>
/// that first forwards every event to an "inner" dispatcher (the one
/// the host app provides, if any) and then, for recognized ticket /
/// reply events, kicks off <see cref="WorkflowRunnerService"/> so
/// admin-configured Workflows fire automatically.
///
/// Runner failures are caught and logged so a misbehaving workflow
/// never blocks the event that fired it. Mirrors the NestJS
/// workflow.listener.ts and the Spring / WordPress WorkflowListener.
///
/// Wired up via <c>services.AddEscalatedWorkflows()</c> — see
/// <see cref="EscalatedWorkflowsServiceCollectionExtensions"/>.
/// </summary>
public class WorkflowEventDispatcher : IEscalatedEventDispatcher
{
    private readonly IEscalatedEventDispatcher _inner;
    private readonly WorkflowRunnerService _runner;
    private readonly ILogger<WorkflowEventDispatcher> _logger;

    public WorkflowEventDispatcher(
        IEscalatedEventDispatcher inner,
        WorkflowRunnerService runner,
        ILogger<WorkflowEventDispatcher> logger)
    {
        _inner = inner;
        _runner = runner;
        _logger = logger;
    }

    public async Task DispatchAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : class
    {
        // Always forward to the host's dispatcher first so its
        // subscribers observe the event in order.
        await _inner.DispatchAsync(@event, ct);

        var (trigger, ticket) = Resolve(@event);
        if (trigger == null || ticket == null)
        {
            return;
        }

        try
        {
            await _runner.RunForEventAsync(trigger, ticket, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[WorkflowEventDispatcher] {Trigger} handler failed on ticket #{TicketId}",
                trigger, ticket.Id);
        }
    }

    /// <summary>
    /// Map a domain event to its canonical workflow trigger name and
    /// the ticket it targets. Returns <c>(null, null)</c> for events
    /// that aren't surfaced to workflows (e.g. SLA warnings, internal
    /// notes).
    /// </summary>
    internal static (string? Trigger, Ticket? Ticket) Resolve(object @event) => @event switch
    {
        TicketCreatedEvent e => ("ticket.created", e.Ticket),
        TicketUpdatedEvent e => ("ticket.updated", e.Ticket),
        TicketStatusChangedEvent e => ("ticket.status_changed", e.Ticket),
        TicketResolvedEvent e => ("ticket.status_changed", e.Ticket),
        TicketClosedEvent e => ("ticket.status_changed", e.Ticket),
        TicketReopenedEvent e => ("ticket.reopened", e.Ticket),
        TicketAssignedEvent e => ("ticket.assigned", e.Ticket),
        TicketPriorityChangedEvent e => ("ticket.priority_changed", e.Ticket),
        DepartmentChangedEvent e => ("ticket.department_changed", e.Ticket),
        TagAddedEvent e => ("ticket.tagged", e.Ticket),
        TagRemovedEvent e => ("ticket.tagged", e.Ticket),
        ReplyCreatedEvent e => ("reply.created", e.Reply.Ticket),
        SlaBreachedEvent e => ("sla.breached", e.Ticket),
        SlaWarningEvent e => ("sla.warning", e.Ticket),
        _ => (null, null),
    };
}
