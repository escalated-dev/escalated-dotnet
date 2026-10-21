using System.ComponentModel.DataAnnotations;

namespace Escalated.Models;

public class EscalationRule
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    [MaxLength(50)]
    public string? Category { get; set; }

    /// <summary>
    /// JSON array of condition objects, e.g. [{"field":"age_hours","value":"4"},{"field":"priority","value":"high"}]
    /// </summary>
    public string Conditions { get; set; } = "[]";

    /// <summary>
    /// JSON array of action objects, e.g. [{"type":"escalate"},{"type":"change_priority","value":"urgent"}]
    /// </summary>
    public string Actions { get; set; } = "[]";

    /// <summary>What starts the rule. The escalation service runs <c>time_based</c> rules on its schedule.</summary>
    [MaxLength(50)]
    public string TriggerType { get; set; } = "time_based";

    /// <summary>Rules run in ascending order.</summary>
    public int Order { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
