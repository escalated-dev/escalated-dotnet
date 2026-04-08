namespace Escalated.Enums;

public enum TicketPriority
{
    Low,
    Medium,
    High,
    Urgent,
    Critical
}

public static class TicketPriorityExtensions
{
    public static string ToValue(this TicketPriority priority) => priority switch
    {
        TicketPriority.Low => "low",
        TicketPriority.Medium => "medium",
        TicketPriority.High => "high",
        TicketPriority.Urgent => "urgent",
        TicketPriority.Critical => "critical",
        _ => "medium"
    };

    public static string Label(this TicketPriority priority) => priority switch
    {
        TicketPriority.Low => "Low",
        TicketPriority.Medium => "Medium",
        TicketPriority.High => "High",
        TicketPriority.Urgent => "Urgent",
        TicketPriority.Critical => "Critical",
        _ => "Unknown"
    };

    public static string Color(this TicketPriority priority) => priority switch
    {
        TicketPriority.Low => "#6B7280",
        TicketPriority.Medium => "#3B82F6",
        TicketPriority.High => "#F59E0B",
        TicketPriority.Urgent => "#F97316",
        TicketPriority.Critical => "#EF4444",
        _ => "#6B7280"
    };

    public static int NumericWeight(this TicketPriority priority) => priority switch
    {
        TicketPriority.Low => 1,
        TicketPriority.Medium => 2,
        TicketPriority.High => 3,
        TicketPriority.Urgent => 4,
        TicketPriority.Critical => 5,
        _ => 2
    };

    public static TicketPriority Parse(string value) => value switch
    {
        "low" => TicketPriority.Low,
        "medium" => TicketPriority.Medium,
        "high" => TicketPriority.High,
        "urgent" => TicketPriority.Urgent,
        "critical" => TicketPriority.Critical,
        _ => TicketPriority.Medium
    };
}
