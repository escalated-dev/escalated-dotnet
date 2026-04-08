using System.ComponentModel.DataAnnotations;

namespace Escalated.Models;

public class Automation
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>
    /// JSON array of conditions
    /// </summary>
    public string Conditions { get; set; } = "[]";

    /// <summary>
    /// JSON array of actions
    /// </summary>
    public string Actions { get; set; } = "[]";

    public bool Active { get; set; } = true;

    public int Position { get; set; }

    public DateTime? LastRunAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
