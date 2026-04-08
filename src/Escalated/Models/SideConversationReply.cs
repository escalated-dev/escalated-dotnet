using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Escalated.Models;

public class SideConversationReply
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int SideConversationId { get; set; }

    [Required]
    public string Body { get; set; } = string.Empty;

    public int? AuthorId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(SideConversationId))]
    public SideConversation? SideConversation { get; set; }
}
