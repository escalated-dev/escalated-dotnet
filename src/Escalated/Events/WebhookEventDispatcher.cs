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
/// NestJS <c>WebhookService</c> event handler. Event names are the shared
/// vocabulary in <see cref="EventNames"/> (ADR 2026-10-21); a subscription stored
/// under a legacy name receives the event under its canonical name.</para>
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
        TicketCreatedEvent => EventNames.TicketCreated,
        TicketUpdatedEvent => EventNames.TicketUpdated,
        TicketStatusChangedEvent => EventNames.TicketStatusChanged,
        TicketResolvedEvent => EventNames.TicketResolved,
        TicketClosedEvent => EventNames.TicketClosed,
        TicketReopenedEvent => EventNames.TicketReopened,
        TicketAssignedEvent => EventNames.TicketAssigned,
        TicketUnassignedEvent => EventNames.TicketUnassigned,
        TicketEscalatedEvent => EventNames.TicketEscalated,
        TicketPriorityChangedEvent => EventNames.TicketPriorityChanged,
        DepartmentChangedEvent => EventNames.TicketDepartmentChanged,
        TagAddedEvent => EventNames.TicketTagAdded,
        TagRemovedEvent => EventNames.TicketTagRemoved,
        ReplyCreatedEvent => EventNames.ReplyCreated,
        InternalNoteAddedEvent => EventNames.NoteCreated,
        SlaBreachedEvent => EventNames.SlaBreached,
        SlaWarningEvent => EventNames.SlaWarning,
        ChatSessionStartedEvent => EventNames.ChatStarted,
        ChatSessionAcceptedEvent => EventNames.ChatAssigned,
        ChatSessionTransferredEvent => EventNames.ChatTransferred,
        ChatMessageEvent => EventNames.ChatMessage,
        ChatSessionEndedEvent => EventNames.ChatEnded,
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
            case ChatSessionStartedEvent e:
                AddChat(payload, e.Session);
                break;
            case ChatSessionAcceptedEvent e:
                AddChat(payload, e.Session);
                break;
            case ChatSessionEndedEvent e:
                AddChat(payload, e.Session);
                break;
            case ChatSessionTransferredEvent e:
                var transferred = AddChat(payload, e.Session);
                transferred["from_agent_id"] = e.FromAgentId;
                transferred["to_agent_id"] = e.ToAgentId;
                transferred["to_department_id"] = e.ToDepartmentId;
                break;
            case ChatMessageEvent e:
                var chat = AddChat(payload, e.Session);
                chat["reply_id"] = e.Reply.Id;
                chat["author_type"] = e.AuthorType;
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

    /// <summary>
    /// The chat session fields that leave the process. The visitor token hash and
    /// the visitor's email never do.
    /// </summary>
    private static Dictionary<string, object?> AddChat(Dictionary<string, object?> payload, ChatSession session)
    {
        var chat = new Dictionary<string, object?>
        {
            ["session_id"] = session.Id,
            ["ticket_id"] = session.TicketId,
            ["status"] = session.Status,
            ["agent_id"] = session.AgentId,
            ["department_id"] = session.DepartmentId,
        };
        payload["chat"] = chat;
        return chat;
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
