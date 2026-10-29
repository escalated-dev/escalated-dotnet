using System.Globalization;
using System.Text.Json.Nodes;
using Escalated.Data;
using Escalated.Enums;
using Escalated.Events;
using Escalated.Models;
using Escalated.Services;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Plugins;

/// <summary>
/// The snake_case objects plugins are given, in event payloads and from the
/// <c>ctx.*</c> reads. Never a guest token, access hash, visitor chat token or
/// visitor email.
/// </summary>
public sealed class PluginPayloads
{
    private readonly EscalatedDbContext _db;

    public PluginPayloads(EscalatedDbContext db)
    {
        _db = db;
    }

    public async Task<JsonObject> TicketAsync(Ticket ticket, CancellationToken ct = default)
    {
        var tags = await _db.TicketTags.AsNoTracking().Where(tt => tt.TicketId == ticket.Id && tt.Tag != null)
            .OrderBy(tt => tt.TagId).Select(tt => tt.Tag!).ToListAsync(ct);

        return new JsonObject
        {
            ["id"] = ticket.Id,
            ["reference"] = ticket.Reference,
            ["subject"] = ticket.Subject,
            ["title"] = ticket.Subject,
            ["description"] = ticket.Description,
            ["status"] = ticket.Status.ToValue(),
            ["priority"] = ticket.Priority.ToValue(),
            ["channel"] = null,
            ["ticket_type"] = ticket.TicketType,
            ["department_id"] = ticket.DepartmentId,
            ["assigned_to"] = ticket.AssignedTo,
            ["requester_id"] = ticket.RequesterId,
            ["contact_id"] = ticket.ContactId,
            ["metadata"] = Metadata(ticket.Metadata),
            ["tags"] = new JsonArray(tags.Select(t => (JsonNode?)Tag(t)).ToArray()),
            ["created_at"] = Date(ticket.CreatedAt),
            ["updated_at"] = Date(ticket.UpdatedAt),
        };
    }

    public static JsonObject Reply(Reply reply) => new()
    {
        ["id"] = reply.Id,
        ["ticket_id"] = reply.TicketId,
        ["body"] = reply.Body,
        ["type"] = reply.Type ?? (reply.IsInternalNote ? "note" : "reply"),
        ["is_internal"] = reply.IsInternalNote,
        ["user_id"] = reply.AuthorId,
        ["created_at"] = Date(reply.CreatedAt),
        ["updated_at"] = Date(reply.UpdatedAt),
    };

    public static JsonObject Tag(Tag tag) => new() { ["id"] = tag.Id, ["name"] = tag.Name, ["slug"] = tag.Slug };

    public static JsonObject TagWithColor(Tag tag)
    {
        var json = Tag(tag);
        json["color"] = tag.Color;
        return json;
    }

    public static JsonObject Department(Department department) => new()
    {
        ["id"] = department.Id,
        ["name"] = department.Name,
        ["slug"] = department.Slug,
        ["description"] = department.Description,
        ["is_active"] = department.IsActive,
    };

    public static JsonObject Contact(Contact contact) => new()
    {
        ["id"] = contact.Id,
        ["name"] = contact.Name,
        ["email"] = contact.Email,
        ["user_id"] = contact.UserId,
        ["created_at"] = Date(contact.CreatedAt),
        ["updated_at"] = Date(contact.UpdatedAt),
    };

    /// <summary><c>{session}</c> for a chat event: the session's own fields, never the visitor's token or email.</summary>
    public static JsonObject Chat(ChatSession session, JsonObject? extras = null)
    {
        var json = new JsonObject
        {
            ["session_id"] = session.Id,
            ["ticket_id"] = session.TicketId,
            ["status"] = session.Status,
            ["agent_id"] = session.AgentId,
            ["department_id"] = session.DepartmentId,
        };
        foreach (var (key, value) in extras ?? new JsonObject()) json[key] = value?.DeepClone();
        return new JsonObject { ["session"] = json };
    }

    /// <summary>
    /// The payload a canonical event is delivered with (plugin contract, "Actions"):
    /// the ticket flat plus <c>ticket</c> and the event's extras; the reply flat plus
    /// <c>reply</c> and <c>ticket</c>; <c>{session}</c> for chat. Null for an event
    /// plugins are not told about.
    /// </summary>
    public async Task<(string Name, JsonObject Payload)?> ForEventAsync(object @event, CancellationToken ct = default)
    {
        var name = WebhookEventDispatcher.EventName(@event);
        if (name is null) return null;

        switch (@event)
        {
            case ReplyCreatedEvent { Reply: var reply }:
                return (name, await ReplyEventAsync(reply, ct));
            case InternalNoteAddedEvent { Reply: var note }:
                return (name, await ReplyEventAsync(note, ct));
            case ChatSessionStartedEvent e:
                return (name, Chat(e.Session));
            case ChatSessionAcceptedEvent e:
                return (name, Chat(e.Session, new JsonObject { ["agent_id"] = e.AgentId }));
            case ChatSessionEndedEvent e:
                return (name, Chat(e.Session, new JsonObject { ["ended_by"] = e.CauserId }));
            case ChatSessionTransferredEvent e:
                return (name, Chat(e.Session, new JsonObject
                {
                    ["from_agent_id"] = e.FromAgentId,
                    ["to_agent_id"] = e.ToAgentId,
                    ["to_department_id"] = e.ToDepartmentId,
                }));
            case ChatMessageEvent e:
                return (name, Chat(e.Session, new JsonObject { ["reply_id"] = e.Reply.Id, ["author_type"] = e.AuthorType }));
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
        if (ticket is null) return null;

        var payload = await TicketAsync(ticket, ct);
        payload["ticket"] = payload.DeepClone();

        switch (@event)
        {
            case TicketStatusChangedEvent e:
                payload["old_status"] = e.OldStatus.ToValue();
                payload["new_status"] = e.NewStatus.ToValue();
                break;
            case TicketAssignedEvent e:
                payload["agent_id"] = e.AgentId;
                break;
            case TicketUnassignedEvent e:
                payload["previous_agent_id"] = e.PreviousAgentId;
                break;
            case TicketPriorityChangedEvent e:
                payload["old_priority"] = e.OldPriority.ToValue();
                payload["new_priority"] = e.NewPriority.ToValue();
                break;
            case DepartmentChangedEvent e:
                payload["old_department_id"] = e.OldDepartmentId;
                payload["new_department_id"] = e.NewDepartmentId;
                break;
            case TagAddedEvent e:
                payload["tag"] = await TagByIdAsync(e.TagId, ct);
                break;
            case TagRemovedEvent e:
                payload["tag"] = await TagByIdAsync(e.TagId, ct);
                break;
            case SlaBreachedEvent e:
                payload["type"] = e.BreachType;
                break;
            case SlaWarningEvent e:
                payload["type"] = e.WarningType;
                payload["minutes_remaining"] = e.MinutesRemaining;
                break;
        }

        return (name, payload);
    }

    private async Task<JsonObject> ReplyEventAsync(Reply reply, CancellationToken ct)
    {
        var payload = Reply(reply);
        payload["reply"] = payload.DeepClone();
        var ticket = reply.Ticket ?? await _db.Tickets.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(t => t.Id == reply.TicketId, ct);
        payload["ticket"] = ticket is null ? null : await TicketAsync(ticket, ct);
        return payload;
    }

    private async Task<JsonObject> TagByIdAsync(int id, CancellationToken ct)
    {
        var tag = await _db.Tags.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
        return tag is null ? new JsonObject { ["id"] = id, ["name"] = null, ["slug"] = null } : Tag(tag);
    }

    private static JsonNode Metadata(string? metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata)) return new JsonObject();
        try
        {
            return JsonNode.Parse(metadata) ?? new JsonObject();
        }
        catch (System.Text.Json.JsonException)
        {
            return new JsonObject();
        }
    }

    public static string? Date(DateTime? value)
    {
        if (value is null) return null;
        var utc = value.Value.Kind switch
        {
            DateTimeKind.Local => value.Value.ToUniversalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc),
            _ => value.Value,
        };
        return utc.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture);
    }
}
