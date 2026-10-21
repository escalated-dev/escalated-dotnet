using Escalated.Data;
using Escalated.Models;
using Escalated.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Escalated.Events;

/// <summary>
/// Bridges the domain event stream to the Workflow engine: each mapped event is translated to
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
/// <see cref="EscalatedEventDispatcher"/> calls it for every event. A host
/// registering its own <see cref="IEscalatedEventDispatcher"/> adds a listener
/// and leaves Workflows running.
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
            TicketCreatedEvent e => (EventNames.TicketCreated, e.Ticket.Id),
            TicketUpdatedEvent e => (EventNames.TicketUpdated, e.Ticket.Id),
            TicketStatusChangedEvent e => (EventNames.TicketStatusChanged, e.Ticket.Id),
            TicketResolvedEvent e => (EventNames.TicketResolved, e.Ticket.Id),
            TicketClosedEvent e => (EventNames.TicketClosed, e.Ticket.Id),
            TicketReopenedEvent e => (EventNames.TicketReopened, e.Ticket.Id),
            TicketAssignedEvent e => (EventNames.TicketAssigned, e.Ticket.Id),
            TicketUnassignedEvent e => (EventNames.TicketUnassigned, e.Ticket.Id),
            TicketEscalatedEvent e => (EventNames.TicketEscalated, e.Ticket.Id),
            TicketPriorityChangedEvent e => (EventNames.TicketPriorityChanged, e.Ticket.Id),
            DepartmentChangedEvent e => (EventNames.TicketDepartmentChanged, e.Ticket.Id),
            TagAddedEvent e => (EventNames.TicketTagAdded, e.Ticket.Id),
            TagRemovedEvent e => (EventNames.TicketTagRemoved, e.Ticket.Id),
            ReplyCreatedEvent e => (EventNames.ReplyCreated, e.Reply.TicketId),
            InternalNoteAddedEvent e => (EventNames.NoteCreated, e.Reply.TicketId),
            SlaWarningEvent e => (EventNames.SlaWarning, e.Ticket.Id),
            SlaBreachedEvent e => (EventNames.SlaBreached, e.Ticket.Id),
            ChatSessionStartedEvent e => (EventNames.ChatStarted, e.Session.TicketId),
            ChatSessionAcceptedEvent e => (EventNames.ChatAssigned, e.Session.TicketId),
            ChatMessageEvent e => (EventNames.ChatMessage, e.Session.TicketId),
            ChatSessionEndedEvent e => (EventNames.ChatEnded, e.Session.TicketId),
            _ => null,
        };
}
