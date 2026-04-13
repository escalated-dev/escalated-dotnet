using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Escalated.Enums;

namespace Escalated.Models;

public class TicketActivity
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int TicketId { get; set; }

    [Required]
    public ActivityType Type { get; set; }

    // Polymorphic causer
    [MaxLength(255)]
    public string? CauserType { get; set; }
    public int? CauserId { get; set; }

    public string? Properties { get; set; } // JSON

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Human-readable relative timestamp expected by the frontend.</summary>
    [NotMapped]
    [JsonPropertyName("created_at_human")]
    public string CreatedAtHuman => FormatHuman(CreatedAt);

    [ForeignKey(nameof(TicketId))]
    public Ticket? Ticket { get; set; }

    private static string FormatHuman(DateTime utc)
    {
        var span = DateTime.UtcNow - utc;
        if (span.TotalSeconds < 60) return "just now";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
        if (span.TotalDays < 7) return $"{(int)span.TotalDays}d ago";
        return utc.ToString("MMM d, yyyy");
    }
}
