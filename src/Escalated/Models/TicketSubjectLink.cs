using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Escalated.Models;

/// <summary>
/// Join row linking a ticket to one host-app subject model (Project, Customer, …).
/// Presentation is supplied by the host via <see cref="Contracts.ITicketSubject"/>.
/// </summary>
public class TicketSubjectLink
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int TicketId { get; set; }

    [Required]
    [MaxLength(255)]
    public string SubjectType { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string SubjectId { get; set; } = string.Empty;

    [MaxLength(255)]
    public string? Role { get; set; }

    public int Position { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(TicketId))]
    public Ticket? Ticket { get; set; }
}
