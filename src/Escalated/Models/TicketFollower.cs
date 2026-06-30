using System.ComponentModel.DataAnnotations;

namespace Escalated.Models;

/// <summary>
/// A host user following a ticket — a notification target alongside the
/// assignee and requester. Recorded via the add_follower workflow action.
/// Unique per (TicketId, UserId). See issue #92.
/// </summary>
public class TicketFollower
{
    [Key]
    public int Id { get; set; }

    public int TicketId { get; set; }

    [Required]
    [MaxLength(255)]
    public string UserId { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
