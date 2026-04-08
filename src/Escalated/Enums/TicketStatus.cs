namespace Escalated.Enums;

public enum TicketStatus
{
    Open,
    InProgress,
    WaitingOnCustomer,
    WaitingOnAgent,
    Escalated,
    Resolved,
    Closed,
    Reopened
}

public static class TicketStatusExtensions
{
    public static string ToValue(this TicketStatus status) => status switch
    {
        TicketStatus.Open => "open",
        TicketStatus.InProgress => "in_progress",
        TicketStatus.WaitingOnCustomer => "waiting_on_customer",
        TicketStatus.WaitingOnAgent => "waiting_on_agent",
        TicketStatus.Escalated => "escalated",
        TicketStatus.Resolved => "resolved",
        TicketStatus.Closed => "closed",
        TicketStatus.Reopened => "reopened",
        _ => "open"
    };

    public static string Label(this TicketStatus status) => status switch
    {
        TicketStatus.Open => "Open",
        TicketStatus.InProgress => "In Progress",
        TicketStatus.WaitingOnCustomer => "Waiting on Customer",
        TicketStatus.WaitingOnAgent => "Waiting on Agent",
        TicketStatus.Escalated => "Escalated",
        TicketStatus.Resolved => "Resolved",
        TicketStatus.Closed => "Closed",
        TicketStatus.Reopened => "Reopened",
        _ => "Unknown"
    };

    public static string Color(this TicketStatus status) => status switch
    {
        TicketStatus.Open => "#3B82F6",
        TicketStatus.InProgress => "#8B5CF6",
        TicketStatus.WaitingOnCustomer => "#F59E0B",
        TicketStatus.WaitingOnAgent => "#F97316",
        TicketStatus.Escalated => "#EF4444",
        TicketStatus.Resolved => "#10B981",
        TicketStatus.Closed => "#6B7280",
        TicketStatus.Reopened => "#3B82F6",
        _ => "#6B7280"
    };

    public static bool IsOpen(this TicketStatus status) =>
        status != TicketStatus.Resolved && status != TicketStatus.Closed;

    public static bool CanTransitionTo(this TicketStatus current, TicketStatus target)
    {
        var allowed = current switch
        {
            TicketStatus.Open => new[]
            {
                TicketStatus.InProgress, TicketStatus.WaitingOnCustomer, TicketStatus.WaitingOnAgent,
                TicketStatus.Escalated, TicketStatus.Resolved, TicketStatus.Closed
            },
            TicketStatus.InProgress => new[]
            {
                TicketStatus.Open, TicketStatus.WaitingOnCustomer, TicketStatus.WaitingOnAgent,
                TicketStatus.Escalated, TicketStatus.Resolved, TicketStatus.Closed
            },
            TicketStatus.WaitingOnCustomer => new[]
            {
                TicketStatus.Open, TicketStatus.InProgress, TicketStatus.WaitingOnAgent,
                TicketStatus.Escalated, TicketStatus.Resolved, TicketStatus.Closed
            },
            TicketStatus.WaitingOnAgent => new[]
            {
                TicketStatus.Open, TicketStatus.InProgress, TicketStatus.WaitingOnCustomer,
                TicketStatus.Escalated, TicketStatus.Resolved, TicketStatus.Closed
            },
            TicketStatus.Escalated => new[]
            {
                TicketStatus.Open, TicketStatus.InProgress, TicketStatus.Resolved, TicketStatus.Closed
            },
            TicketStatus.Resolved => new[] { TicketStatus.Reopened, TicketStatus.Closed },
            TicketStatus.Closed => new[] { TicketStatus.Reopened },
            TicketStatus.Reopened => new[]
            {
                TicketStatus.Open, TicketStatus.InProgress, TicketStatus.WaitingOnCustomer,
                TicketStatus.WaitingOnAgent, TicketStatus.Escalated, TicketStatus.Resolved,
                TicketStatus.Closed
            },
            _ => Array.Empty<TicketStatus>()
        };

        return allowed.Contains(target);
    }

    public static TicketStatus Parse(string value) => value switch
    {
        "open" => TicketStatus.Open,
        "in_progress" => TicketStatus.InProgress,
        "waiting_on_customer" => TicketStatus.WaitingOnCustomer,
        "waiting_on_agent" => TicketStatus.WaitingOnAgent,
        "escalated" => TicketStatus.Escalated,
        "resolved" => TicketStatus.Resolved,
        "closed" => TicketStatus.Closed,
        "reopened" => TicketStatus.Reopened,
        _ => TicketStatus.Open
    };
}
