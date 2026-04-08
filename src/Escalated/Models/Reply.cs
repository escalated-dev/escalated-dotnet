using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Escalated.Models;

public class Reply
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int TicketId { get; set; }

    [Required]
    public string Body { get; set; } = string.Empty;

    public bool IsInternalNote { get; set; }
    public bool IsPinned { get; set; }

    [MaxLength(50)]
    public string? Type { get; set; } // "reply" or "note"

    // Polymorphic author
    [MaxLength(255)]
    public string? AuthorType { get; set; }
    public int? AuthorId { get; set; }

    // Email threading
    [MaxLength(255)]
    public string? MessageId { get; set; }
    [MaxLength(255)]
    public string? InReplyTo { get; set; }

    public string? Metadata { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; set; }

    // Navigation
    [ForeignKey(nameof(TicketId))]
    public Ticket? Ticket { get; set; }

    public ICollection<Attachment> Attachments { get; set; } = new List<Attachment>();
}
