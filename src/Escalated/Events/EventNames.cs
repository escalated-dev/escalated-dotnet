namespace Escalated.Events;

/// <summary>
/// The shared event vocabulary (escalated-developer-context ADR
/// <c>2026-10-21-event-vocabulary</c>): the only names Escalated emits to
/// outbound webhooks and fires Workflows on, and the legacy names it still
/// accepts when it reads a stored webhook subscription or Workflow trigger.
/// </summary>
public static class EventNames
{
    public const string TicketCreated = "ticket.created";
    public const string TicketUpdated = "ticket.updated";
    public const string TicketStatusChanged = "ticket.status_changed";
    public const string TicketResolved = "ticket.resolved";
    public const string TicketClosed = "ticket.closed";
    public const string TicketReopened = "ticket.reopened";
    public const string TicketAssigned = "ticket.assigned";
    public const string TicketUnassigned = "ticket.unassigned";
    public const string TicketEscalated = "ticket.escalated";
    public const string TicketPriorityChanged = "ticket.priority_changed";
    public const string TicketDepartmentChanged = "ticket.department_changed";
    public const string TicketTagAdded = "ticket.tag_added";
    public const string TicketTagRemoved = "ticket.tag_removed";
    public const string ReplyCreated = "reply.created";
    public const string NoteCreated = "note.created";
    public const string SlaWarning = "sla.warning";
    public const string SlaBreached = "sla.breached";
    public const string ChatStarted = "chat.started";
    public const string ChatAssigned = "chat.assigned";
    public const string ChatTransferred = "chat.transferred";
    public const string ChatMessage = "chat.message";
    public const string ChatEnded = "chat.ended";

    /// <summary>Every canonical name, in the ADR's order.</summary>
    public static readonly IReadOnlyList<string> Canonical = new[]
    {
        TicketCreated, TicketUpdated, TicketStatusChanged, TicketResolved, TicketClosed, TicketReopened,
        TicketAssigned, TicketUnassigned, TicketEscalated, TicketPriorityChanged, TicketDepartmentChanged,
        TicketTagAdded, TicketTagRemoved, ReplyCreated, NoteCreated, SlaWarning, SlaBreached,
        ChatStarted, ChatAssigned, ChatTransferred, ChatMessage, ChatEnded,
    };

    /// <summary>Legacy name to canonical name. Read, never emitted or offered.</summary>
    public static readonly IReadOnlyDictionary<string, string> Legacy = new Dictionary<string, string>
    {
        ["ticket.replied"] = ReplyCreated,
        ["internal_note.added"] = NoteCreated,
        ["internal_note.created"] = NoteCreated,
        ["tag.added"] = TicketTagAdded,
        ["ticket.tagged"] = TicketTagAdded,
        ["tag.removed"] = TicketTagRemoved,
        ["ticket.untagged"] = TicketTagRemoved,
        ["chat.session_started"] = ChatStarted,
        ["chat.session_ended"] = ChatEnded,
    };

    /// <summary>The canonical name for a stored name; names that are not legacy are returned unchanged.</summary>
    public static string Canonicalize(string name)
    {
        var trimmed = name.Trim();
        return Legacy.TryGetValue(trimmed, out var canonical) ? canonical : trimmed;
    }

    /// <summary>A canonical name and every legacy name that reads as it.</summary>
    public static IReadOnlyList<string> NamesFor(string canonical) =>
        Legacy.Where(pair => pair.Value == canonical).Select(pair => pair.Key).Prepend(canonical).ToList();
}
