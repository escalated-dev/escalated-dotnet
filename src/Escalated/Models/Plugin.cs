using System.ComponentModel.DataAnnotations;

namespace Escalated.Models;

/// <summary>
/// The activation state of one plugin (<c>escalated_plugins</c>, the plugin
/// contract's Storage). A registered plugin with no row is inactive.
/// </summary>
public class Plugin
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(255)]
    public string Slug { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public DateTime? ActivatedAt { get; set; }
    public DateTime? DeactivatedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
