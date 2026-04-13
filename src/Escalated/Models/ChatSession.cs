using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Escalated.Enums;

namespace Escalated.Models;

/// <summary>
/// Represents a live chat session. Under the hood a chat is a Ticket
/// with status "live" and channel "chat"; this entity tracks the
/// real-time session metadata that sits alongside that ticket.
/// </summary>
public class ChatSession
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// The underlying ticket that stores the conversation history.
    /// </summary>
    public int TicketId { get; set; }

    /// <summary>
    /// Visitor display name (may be anonymous).
    /// </summary>
    [MaxLength(255)]
    public string VisitorName { get; set; } = "Visitor";

    /// <summary>
    /// Visitor email (optional).
    /// </summary>
    [MaxLength(255)]
    public string? VisitorEmail { get; set; }

    /// <summary>
    /// Agent user ID currently handling the chat, if any.
    /// </summary>
    public int? AgentId { get; set; }

    /// <summary>
    /// Department the chat is routed to.
    /// </summary>
    public int? DepartmentId { get; set; }

    [MaxLength(30)]
    public string Status { get; set; } = "waiting"; // waiting, active, ended

    public DateTime? AcceptedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Arbitrary JSON metadata from the chat widget (e.g. page URL, browser info).
    /// </summary>
    public string? Metadata { get; set; }

    // Navigation
    [ForeignKey(nameof(TicketId))]
    public Ticket? Ticket { get; set; }

    [ForeignKey(nameof(DepartmentId))]
    public Department? Department { get; set; }
}
