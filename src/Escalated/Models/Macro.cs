using System.ComponentModel.DataAnnotations;

namespace Escalated.Models;

public class Macro
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>
    /// JSON array of actions, e.g. [{"type":"status","value":"resolved"},{"type":"reply","value":"Thank you!"}]
    /// </summary>
    public string Actions { get; set; } = "[]";

    public int? CreatedBy { get; set; }

    public bool IsShared { get; set; } = true;

    public int Order { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
