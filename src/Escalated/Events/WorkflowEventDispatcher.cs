using Escalated.Data;
using Escalated.Models;
using Escalated.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Escalated.Events;

/// <summary>
/// The default <see cref="IEscalatedEventDispatcher"/>. Bridges the domain
/// event stream to the Workflow engine: each mapped event is translated to
/// the frontend-facing trigger-event string and handed to
/// <see cref="WorkflowRunnerService"/>, which evaluates and executes every
/// active Workflow configured for that trigger.
///
/// Mirrors the NestJS reference <c>WorkflowListener</c> and the Laravel
/// <c>ProcessWorkflows</c> listener.
///
/// The runner (and its scoped dependencies — <see cref="EscalatedDbContext"/>,
/// <see cref="WorkflowExecutorService"/>, <see cref="TicketService"/>) are
/// resolved from a fresh DI scope per dispatch, so this dispatcher is safe to
/// register as a singleton and safe to invoke from inside a request scope. The
/// ticket is reloaded by id inside that scope (the triggering mutation always
/// persists before dispatching), keeping all Workflow side-effects on a single
/// context.
///
/// A re-entrancy guard prevents an event storm: a Workflow action (e.g.
/// <c>change_priority</c>) itself re-dispatches domain events, but those nested
/// dispatches are skipped so a Workflow can never cascade into itself in an
/// unbounded loop.
///
/// Host apps can opt out of Workflow processing entirely by registering their
/// own <see cref="IEscalatedEventDispatcher"/> (for example
/// <see cref="NullEventDispatcher"/>) before/after calling <c>AddEscalated</c>.
/// </summary>
public class WorkflowEventDispatcher : IEscalatedEventDispatcher
{
    // Flows across the async call chain (and DI scope boundaries) so that
    // events raised while a Workflow is executing are recognised as nested
    // and skipped.
    private static readonly AsyncLocal<bool> Running = new();

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WorkflowEventDispatcher> _logger;

    public WorkflowEventDispatcher(
        IServiceScopeFactory scopeFactory,
        ILogger<WorkflowEventDispatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task DispatchAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : class
    {
        // Skip nested dispatches raised by a Workflow's own actions so a
        // Workflow can never trigger itself in an unbounded cascade.
        if (Running.Value) return;

        var mapping = Resolve(@event);
        if (mapping is null) return;

        var (triggerEvent, ticketId) = mapping.Value;
        if (ticketId <= 0) return;

        Running.Value = true;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EscalatedDbContext>();

            var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Id == ticketId, ct);
            if (ticket is null) return;

            var runner = scope.ServiceProvider.GetRequiredService<WorkflowRunnerService>();
            await runner.RunForEventAsync(triggerEvent, ticket, ct);
        }
        catch (Exception ex)
        {
            // A misbehaving Workflow must never break the triggering mutation.
            _logger.LogError(ex,
                "[WorkflowEventDispatcher] failed running workflows for {Event}",
                typeof(TEvent).Name);
        }
        finally
        {
            Running.Value = false;
        }
    }

    /// <summary>
    /// Maps a domain event to its (trigger-event string, ticket id) pair.
    /// Events that don't drive a Workflow trigger return <c>null</c> and are
    /// ignored. Mirrors <c>WorkflowListener</c> / <c>ProcessWorkflows::resolveEvent</c>,
    /// covering the trigger set declared on <see cref="WorkflowEngine.TriggerEvents"/>.
    /// </summary>
    private static (string TriggerEvent, int TicketId)? Resolve<TEvent>(TEvent @event)
        where TEvent : class => @event switch
        {
            TicketCreatedEvent e => ("ticket.created", e.Ticket.Id),
            TicketUpdatedEvent e => ("ticket.updated", e.Ticket.Id),
            TicketStatusChangedEvent e => ("ticket.status_changed", e.Ticket.Id),
            TicketReopenedEvent e => ("ticket.reopened", e.Ticket.Id),
            TicketAssignedEvent e => ("ticket.assigned", e.Ticket.Id),
            TicketPriorityChangedEvent e => ("ticket.priority_changed", e.Ticket.Id),
            DepartmentChangedEvent e => ("ticket.department_changed", e.Ticket.Id),
            TagAddedEvent e => ("ticket.tagged", e.Ticket.Id),
            ReplyCreatedEvent e => ("reply.created", e.Reply.TicketId),
            SlaWarningEvent e => ("sla.warning", e.Ticket.Id),
            SlaBreachedEvent e => ("sla.breached", e.Ticket.Id),
            _ => null,
        };
}
