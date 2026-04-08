using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
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

    [ForeignKey(nameof(TicketId))]
    public Ticket? Ticket { get; set; }
}
