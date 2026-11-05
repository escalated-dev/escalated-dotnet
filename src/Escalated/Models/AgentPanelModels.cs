using System.ComponentModel.DataAnnotations;

namespace Escalated.Models;

/// <summary>
/// When an agent last read a ticket (ADR 2026-11-05-agent-panel-data). A null
/// <see cref="LastReadAt"/> is a ticket the agent marked unread. Unique per agent and ticket.
/// </summary>
public class TicketRead
{
    [Key]
    public int Id { get; set; }

    public int TicketId { get; set; }

    [Required]
    [MaxLength(255)]
    public string UserId { get; set; } = string.Empty;

    public DateTime? LastReadAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// When a customer was last seen on the customer portal or the widget (ADR
/// 2026-11-05-agent-panel-data). The subject is a host user (<c>user</c>), a contact
/// (<c>contact</c>), or a guest ticket itself (<c>ticket</c>). Only agent pages ever read it.
/// </summary>
public class CustomerPresence
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(32)]
    public string SubjectType { get; set; } = string.Empty;

    [Required]
    [MaxLength(191)]
    public string SubjectId { get; set; } = string.Empty;

    public DateTime LastSeenAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// An agent's in-app notification (ADR 2026-11-05-agent-panel-data). <see cref="Data"/> is
/// JSON with the ticket id and what the title needs; the feed builds each item when it is
/// read, so a ticket the agent can no longer view is redacted.
/// </summary>
public class AgentNotification
{
    [Key]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    [MaxLength(255)]
    public string UserId { get; set; } = string.Empty;

    [Required]
    [MaxLength(64)]
    public string Type { get; set; } = string.Empty;

    public string Data { get; set; } = "{}";

    public DateTime? ReadAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
