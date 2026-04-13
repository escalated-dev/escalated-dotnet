using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Escalated.Models;

[Table("escalated_workflows")]
public class Workflow
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(255)]
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    [Required]
    [MaxLength(255)]
    [Column("trigger_event")]
    public string TriggerEvent { get; set; } = string.Empty;

    /// <summary>Alias for frontend compatibility: the frontend uses `trigger` instead of `trigger_event`.</summary>
    [NotMapped]
    [JsonPropertyName("trigger")]
    public string Trigger => TriggerEvent;

    /// <summary>JSON-encoded conditions object.</summary>
    public string Conditions { get; set; } = "{}";

    /// <summary>JSON-encoded actions array.</summary>
    public string Actions { get; set; } = "[]";

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    public int Position { get; set; }

    [Column("stop_on_match")]
    public bool StopOnMatch { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<WorkflowLog> WorkflowLogs { get; set; } = new List<WorkflowLog>();
}
