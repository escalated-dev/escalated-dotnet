using Escalated.Data;
using Escalated.Enums;
using Escalated.Models;
using Escalated.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Escalated.Events;

/// <summary>
/// Bridges domain events to outbound webhooks: each mapped event is named and
/// handed to <see cref="WebhookDispatcher"/>, which delivers it to every active
/// webhook subscribed to that name (or to <c>*</c>) and records a
/// <see cref="WebhookDelivery"/> for each attempt.
///
/// <para>Mirrors the Laravel reference <c>DispatchWebhook</c> listener and the
/// NestJS <c>WebhookService</c> event handler. Event names follow the core event
/// list in escalated-developer-context <c>ARCHITECTURE.md</c> where it names the
/// event (so tags are <c>ticket.tagged</c> / <c>ticket.untagged</c>, as the Workflow
/// triggers already are), and the Laravel reference where it does not.</para>
///
/// <para>Resolves <see cref="WebhookDispatcher"/> and its <see cref="EscalatedDbContext"/>
/// from a fresh scope per event, so it is a singleton and never shares a context
/// with the mutation that raised the event. A failing delivery is recorded by the
/// dispatcher and logged here; it never fails the mutation.</para>
/// </summary>
public class WebhookEventDispatcher : IEscalatedEventDispatcher
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WebhookEventDispatcher> _logger;

    public WebhookEventDispatcher(IServiceScopeFactory scopeFactory, ILogger<WebhookEventDispatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task DispatchAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : class
    {
        var eventName = EventName(@event);
        if (eventName is null) return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EscalatedDbContext>();

            // Most events have no subscriber; don't build a payload for nobody.
            if (!await db.Webhooks.AnyAsync(w => w.Active, ct)) return;

            var payload = await PayloadAsync(@event, db, ct);
            await scope.ServiceProvider.GetRequiredService<WebhookDispatcher>().DispatchAsync(eventName, payload, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WebhookEventDispatcher] failed dispatching webhooks for {Event}", eventName);
        }
    }

    /// <summary>The webhook event name a domain event is delivered as, or <c>null</c> for none.</summary>
    public static string? EventName<TEvent>(TEvent @event) where TEvent : class => @event switch
    {
        TicketCreatedEvent => "ticket.created",
        TicketUpdatedEvent => "ticket.updated",
        TicketStatusChangedEvent => "ticket.status_changed",
        TicketResolvedEvent => "ticket.resolved",
        TicketClosedEvent => "ticket.closed",
        TicketReopenedEvent => "ticket.reopened",
        TicketAssignedEvent => "ticket.assigned",
        TicketUnassignedEvent => "ticket.unassigned",
        TicketEscalatedEvent => "ticket.escalated",
        TicketPriorityChangedEvent => "ticket.priority_changed",
        DepartmentChangedEvent => "ticket.department_changed",
        TagAddedEvent => "ticket.tagged",
        TagRemovedEvent => "ticket.untagged",
        ReplyCreatedEvent => "reply.created",
        InternalNoteAddedEvent => "internal_note.added",
        SlaBreachedEvent => "sla.breached",
        SlaWarningEvent => "sla.warning",
        _ => null,
    };

    /// <summary>
    /// The same shape the Laravel reference sends: the ticket's identity and state,
    /// plus the reply, tag or agent the event is about.
    /// </summary>
    private static async Task<Dictionary<string, object?>> PayloadAsync(object @event, EscalatedDbContext db, CancellationToken ct)
    {
        var payload = new Dictionary<string, object?>();

        switch (@event)
        {
            case ReplyCreatedEvent { Reply: var reply }:
                AddReply(payload, reply);
                break;
            case InternalNoteAddedEvent { Reply: var note }:
                AddReply(payload, note);
                break;
        }

        var ticket = @event switch
        {
            TicketCreatedEvent e => e.Ticket,
            TicketUpdatedEvent e => e.Ticket,
            TicketStatusChangedEvent e => e.Ticket,
            TicketResolvedEvent e => e.Ticket,
            TicketClosedEvent e => e.Ticket,
            TicketReopenedEvent e => e.Ticket,
            TicketAssignedEvent e => e.Ticket,
            TicketUnassignedEvent e => e.Ticket,
            TicketEscalatedEvent e => e.Ticket,
            TicketPriorityChangedEvent e => e.Ticket,
            DepartmentChangedEvent e => e.Ticket,
            TagAddedEvent e => e.Ticket,
            TagRemovedEvent e => e.Ticket,
            SlaBreachedEvent e => e.Ticket,
            SlaWarningEvent e => e.Ticket,
            _ => null,
        };

        if (ticket is not null)
        {
            payload["ticket"] = new Dictionary<string, object?>
            {
                ["id"] = ticket.Id,
                ["reference"] = ticket.Reference,
                ["subject"] = ticket.Subject,
                ["status"] = ticket.Status.ToValue(),
                ["priority"] = ticket.Priority.ToValue(),
            };
        }

        var tagId = @event switch
        {
            TagAddedEvent e => e.TagId,
            TagRemovedEvent e => e.TagId,
            _ => (int?)null,
        };

        if (tagId is not null)
        {
            var name = await db.Tags.Where(t => t.Id == tagId).Select(t => t.Name).FirstOrDefaultAsync(ct);
            payload["tag"] = new Dictionary<string, object?> { ["id"] = tagId, ["name"] = name };
        }

        if (@event is TicketAssignedEvent assigned)
        {
            payload["agent_id"] = assigned.AgentId;
        }

        return payload;
    }

    private static void AddReply(Dictionary<string, object?> payload, Reply reply)
    {
        payload["ticket"] = new Dictionary<string, object?>
        {
            ["id"] = reply.TicketId,
            ["reference"] = reply.Ticket?.Reference,
        };
        payload["reply"] = new Dictionary<string, object?>
        {
            ["id"] = reply.Id,
            ["is_internal_note"] = reply.IsInternalNote,
        };
    }
}
