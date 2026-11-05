using System.ComponentModel.DataAnnotations;

namespace Escalated.Models;

public class AgentProfile
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(255)]
    public string UserId { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? AgentType { get; set; } // "full" or "light"

    public int MaxTickets { get; set; } = 50;

    /// <summary>The agent's own status (ADR 2026-11-05-agent-panel-data): online, away or offline.</summary>
    [MaxLength(20)]
    public string ChatStatus { get; set; } = "offline";

    [MaxLength(255)]
    public string? Signature { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public bool IsLightAgent() => AgentType == "light";
}
