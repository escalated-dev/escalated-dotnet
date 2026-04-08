using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Escalated.Models;

public class InboundEmail
{
    [Key]
    public int Id { get; set; }

    [MaxLength(255)]
    public string? MessageId { get; set; }

    [MaxLength(255)]
    public string FromEmail { get; set; } = string.Empty;

    [MaxLength(255)]
    public string? FromName { get; set; }

    [MaxLength(255)]
    public string? ToEmail { get; set; }

    [MaxLength(255)]
    public string? Subject { get; set; }

    public string? BodyText { get; set; }
    public string? BodyHtml { get; set; }

    [MaxLength(255)]
    public string? InReplyTo { get; set; }

    [MaxLength(50)]
    public string? Adapter { get; set; }

    [MaxLength(50)]
    public string Status { get; set; } = "pending"; // pending, processed, failed

    public int? TicketId { get; set; }
    public int? ReplyId { get; set; }

    public DateTime? ProcessedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(TicketId))]
    public Ticket? Ticket { get; set; }
}
