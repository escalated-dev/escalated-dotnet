using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Escalated.Models;

public class SideConversation
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int TicketId { get; set; }

    [Required]
    [MaxLength(255)]
    public string Subject { get; set; } = string.Empty;

    public int? CreatedBy { get; set; }

    [MaxLength(50)]
    public string Status { get; set; } = "open"; // open, closed

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(TicketId))]
    public Ticket? Ticket { get; set; }

    public ICollection<SideConversationReply> Replies { get; set; } = new List<SideConversationReply>();
}
