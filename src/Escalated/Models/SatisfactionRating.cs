using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Escalated.Models;

public class SatisfactionRating
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int TicketId { get; set; }

    // Polymorphic rater
    [MaxLength(255)]
    public string? RaterType { get; set; }
    public int? RaterId { get; set; }

    [Required]
    [Range(1, 5)]
    public int Rating { get; set; }

    public string? Comment { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(TicketId))]
    public Ticket? Ticket { get; set; }
}
