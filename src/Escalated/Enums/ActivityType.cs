namespace Escalated.Enums;

public enum ActivityType
{
    StatusChanged,
    Assigned,
    Unassigned,
    PriorityChanged,
    TagAdded,
    TagRemoved,
    Escalated,
    SlaBreached,
    Replied,
    NoteAdded,
    DepartmentChanged,
    Reopened,
    Resolved,
    Closed,
    TicketSplit,
    TicketMerged,
    TicketLinked,
    Snoozed,
    Unsnoozed
}

public static class ActivityTypeExtensions
{
    public static string ToValue(this ActivityType type) => type switch
    {
        ActivityType.StatusChanged => "status_changed",
        ActivityType.Assigned => "assigned",
        ActivityType.Unassigned => "unassigned",
        ActivityType.PriorityChanged => "priority_changed",
        ActivityType.TagAdded => "tag_added",
        ActivityType.TagRemoved => "tag_removed",
        ActivityType.Escalated => "escalated",
        ActivityType.SlaBreached => "sla_breached",
        ActivityType.Replied => "replied",
        ActivityType.NoteAdded => "note_added",
        ActivityType.DepartmentChanged => "department_changed",
        ActivityType.Reopened => "reopened",
        ActivityType.Resolved => "resolved",
        ActivityType.Closed => "closed",
        ActivityType.TicketSplit => "ticket_split",
        ActivityType.TicketMerged => "ticket_merged",
        ActivityType.TicketLinked => "ticket_linked",
        ActivityType.Snoozed => "snoozed",
        ActivityType.Unsnoozed => "unsnoozed",
        _ => "unknown"
    };

    public static string Label(this ActivityType type) => type switch
    {
        ActivityType.StatusChanged => "Status Changed",
        ActivityType.Assigned => "Assigned",
        ActivityType.Unassigned => "Unassigned",
        ActivityType.PriorityChanged => "Priority Changed",
        ActivityType.TagAdded => "Tag Added",
        ActivityType.TagRemoved => "Tag Removed",
        ActivityType.Escalated => "Escalated",
        ActivityType.SlaBreached => "SLA Breached",
        ActivityType.Replied => "Replied",
        ActivityType.NoteAdded => "Note Added",
        ActivityType.DepartmentChanged => "Department Changed",
        ActivityType.Reopened => "Reopened",
        ActivityType.Resolved => "Resolved",
        ActivityType.Closed => "Closed",
        ActivityType.TicketSplit => "Ticket Split",
        ActivityType.TicketMerged => "Ticket Merged",
        ActivityType.TicketLinked => "Ticket Linked",
        ActivityType.Snoozed => "Snoozed",
        ActivityType.Unsnoozed => "Unsnoozed",
        _ => "Unknown"
    };
}
