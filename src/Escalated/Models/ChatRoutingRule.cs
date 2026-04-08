using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Escalated.Models;

/// <summary>
/// Rules that control how incoming chat sessions are routed to agents
/// or departments based on conditions.
/// </summary>
public class ChatRoutingRule
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(255)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Target department to route to when conditions match.
    /// </summary>
    public int? DepartmentId { get; set; }

    /// <summary>
    /// Target agent to route to when conditions match.
    /// </summary>
    public int? AgentId { get; set; }

    /// <summary>
    /// JSON-encoded conditions (e.g. page URL patterns, visitor metadata).
    /// </summary>
    public string? Conditions { get; set; }

    /// <summary>
    /// Lower priority number = higher precedence.
    /// </summary>
    public int Priority { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    [ForeignKey(nameof(DepartmentId))]
    public Department? Department { get; set; }
}
