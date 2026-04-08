using System.ComponentModel.DataAnnotations;

namespace Escalated.Models;

/// <summary>
/// Custom ticket statuses defined by administrators.
/// </summary>
public class TicketStatusModel
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Label { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Slug { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? Color { get; set; }

    public bool IsDefault { get; set; }

    public int Position { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
