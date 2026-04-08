using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Escalated.Models;

public class TicketLink
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int ParentTicketId { get; set; }

    [Required]
    public int ChildTicketId { get; set; }

    [Required]
    [MaxLength(50)]
    public string LinkType { get; set; } = "related"; // related, split, blocks, duplicates

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(ParentTicketId))]
    public Ticket? ParentTicket { get; set; }

    [ForeignKey(nameof(ChildTicketId))]
    public Ticket? ChildTicket { get; set; }
}
