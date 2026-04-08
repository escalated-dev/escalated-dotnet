using System.ComponentModel.DataAnnotations;

namespace Escalated.Models;

public class AuditLog
{
    [Key]
    public int Id { get; set; }

    public int? UserId { get; set; }

    [Required]
    [MaxLength(255)]
    public string EntityType { get; set; } = string.Empty;

    public int EntityId { get; set; }

    [Required]
    [MaxLength(50)]
    public string Action { get; set; } = string.Empty; // created, updated, deleted

    public string? OldValues { get; set; } // JSON
    public string? NewValues { get; set; } // JSON

    [MaxLength(45)]
    public string? IpAddress { get; set; }

    [MaxLength(500)]
    public string? UserAgent { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
