using System.ComponentModel.DataAnnotations;

namespace Escalated.Models;

public class AgentProfile
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int UserId { get; set; }

    [MaxLength(50)]
    public string? AgentType { get; set; } // "full" or "light"

    public int MaxTickets { get; set; } = 50;

    [MaxLength(255)]
    public string? Signature { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public bool IsLightAgent() => AgentType == "light";
}
