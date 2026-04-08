using System.ComponentModel.DataAnnotations;

namespace Escalated.Models;

public class CannedResponse
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Title { get; set; } = string.Empty;

    [Required]
    public string Body { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Category { get; set; }

    public int? CreatedBy { get; set; }

    public bool IsShared { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
