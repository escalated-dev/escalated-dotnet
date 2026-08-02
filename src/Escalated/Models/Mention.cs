using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Escalated.Models;

/// <summary>
/// A record that a host user was @-mentioned inside an internal note. Mirrors
/// the Laravel reference <c>Escalated\Laravel\Models\Mention</c>: one row per
/// (reply, mentioned user) pair, with an optional <see cref="ReadAt"/> marker
/// so the agent's mention inbox can distinguish unread items.
/// </summary>
public class Mention
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int ReplyId { get; set; }

    /// <summary>
    /// Host user id of the mentioned agent. String to match the host-owned
    /// user table (auth lives in the host app), consistent with
    /// <see cref="Reply.AuthorId"/> and <see cref="TicketFollower.UserId"/>.
    /// </summary>
    [Required]
    [MaxLength(255)]
    public string UserId { get; set; } = string.Empty;

    public DateTime? ReadAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    [ForeignKey(nameof(ReplyId))]
    public Reply? Reply { get; set; }
}
