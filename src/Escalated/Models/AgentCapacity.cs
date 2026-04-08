using System.ComponentModel.DataAnnotations;

namespace Escalated.Models;

public class AgentCapacity
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int UserId { get; set; }

    [MaxLength(50)]
    public string Channel { get; set; } = "default";

    public int MaxConcurrent { get; set; } = 10;

    public int CurrentCount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public bool HasCapacity() => CurrentCount < MaxConcurrent;
}
