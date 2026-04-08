using System.ComponentModel.DataAnnotations;

namespace Escalated.Models;

public class SavedView
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public int? UserId { get; set; }

    /// <summary>
    /// JSON object with filter configuration
    /// </summary>
    public string Filters { get; set; } = "{}";

    [MaxLength(50)]
    public string? SortBy { get; set; }

    [MaxLength(10)]
    public string? SortDir { get; set; }

    public bool IsShared { get; set; }

    public bool IsDefault { get; set; }

    public int Position { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
