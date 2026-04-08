using System.ComponentModel.DataAnnotations;

namespace Escalated.Models;

public class Plugin
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Slug { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public DateTime? ActivatedAt { get; set; }
    public DateTime? DeactivatedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
