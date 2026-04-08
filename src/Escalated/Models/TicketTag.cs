namespace Escalated.Models;

/// <summary>
/// Join table for Ticket-Tag many-to-many relationship.
/// </summary>
public class TicketTag
{
    public int TicketId { get; set; }
    public int TagId { get; set; }

    public Ticket? Ticket { get; set; }
    public Tag? Tag { get; set; }
}
