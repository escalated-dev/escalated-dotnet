using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
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
    [MaxLength(255)]
    public string? AgentId { get; set; }

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

    /// <summary>
    /// SHA-256 (lowercase hex) of the visitor's chat token. The widget reaches
    /// its chat only through that token, returned as <c>id</c> when the chat
    /// starts; the token itself is never stored.
    /// </summary>
    [JsonIgnore]
    [MaxLength(64)]
    public string? VisitorTokenHash { get; set; }

    /// <summary>
    /// When the visitor's chat token stops working (the guest access lifetime
    /// after the chat starts). Null only for chats started before tokens expired;
    /// their next widget request gives them a full lifetime.
    /// </summary>
    [JsonIgnore]
    public DateTime? VisitorTokenExpiresAt { get; set; }

    // Navigation
    [JsonIgnore]
    [ForeignKey(nameof(TicketId))]
    public Ticket? Ticket { get; set; }

    [ForeignKey(nameof(DepartmentId))]
    public Department? Department { get; set; }
}
