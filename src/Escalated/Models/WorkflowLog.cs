using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Escalated.Models;

[Table("escalated_workflow_logs")]
public class WorkflowLog
{
    [Key]
    public int Id { get; set; }

    [Column("workflow_id")]
    public int WorkflowId { get; set; }

    [JsonIgnore]
    [ForeignKey(nameof(WorkflowId))]
    public Workflow? Workflow { get; set; }

    [Column("ticket_id")]
    public int TicketId { get; set; }

    [JsonIgnore]
    [ForeignKey(nameof(TicketId))]
    public Ticket? Ticket { get; set; }

    [Column("trigger_event")]
    [MaxLength(255)]
    public string TriggerEvent { get; set; } = string.Empty;

    [Column("conditions_matched")]
    public bool ConditionsMatched { get; set; } = true;

    /// <summary>Raw JSON array of executed action details.</summary>
    [Column("actions_executed")]
    [JsonIgnore]
    public string ActionsExecutedJson { get; set; } = "[]";

    [Column("error_message")]
    public string? ErrorMessage { get; set; }

    [Column("started_at")]
    public DateTime? StartedAt { get; set; }

    [Column("completed_at")]
    public DateTime? CompletedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // --- Computed fields expected by the frontend ---

    /// <summary>Alias: frontend reads `event` instead of `trigger_event`.</summary>
    [NotMapped]
    [JsonPropertyName("event")]
    public string Event => TriggerEvent;

    /// <summary>Frontend reads `workflow_name` from eager-loaded relationship.</summary>
    [NotMapped]
    [JsonPropertyName("workflow_name")]
    public string? WorkflowName => Workflow?.Name;

    /// <summary>Frontend reads `ticket_reference` from eager-loaded relationship.</summary>
    [NotMapped]
    [JsonPropertyName("ticket_reference")]
    public string? TicketReference => Ticket?.Reference;

    /// <summary>Boolean alias for conditions_matched.</summary>
    [NotMapped]
    [JsonPropertyName("matched")]
    public bool Matched => ConditionsMatched;

    /// <summary>Integer count of executed actions.</summary>
    [NotMapped]
    [JsonPropertyName("actions_executed")]
    public int ActionsExecutedCount
    {
        get
        {
            try
            {
                var arr = JsonSerializer.Deserialize<JsonElement[]>(ActionsExecutedJson ?? "[]");
                return arr?.Length ?? 0;
            }
            catch { return 0; }
        }
    }

    /// <summary>Raw actions array for the expanded detail view.</summary>
    [NotMapped]
    [JsonPropertyName("action_details")]
    public object[] ActionDetails
    {
        get
        {
            try
            {
                return JsonSerializer.Deserialize<object[]>(ActionsExecutedJson ?? "[]") ?? Array.Empty<object>();
            }
            catch { return Array.Empty<object>(); }
        }
    }

    /// <summary>Milliseconds between started_at and completed_at.</summary>
    [NotMapped]
    [JsonPropertyName("duration_ms")]
    public long? DurationMs => StartedAt.HasValue && CompletedAt.HasValue
        ? (long)(CompletedAt.Value - StartedAt.Value).TotalMilliseconds
        : null;

    /// <summary>Computed status: 'failed' when an error is present, otherwise 'success'.</summary>
    [NotMapped]
    [JsonPropertyName("status")]
    public string Status => !string.IsNullOrEmpty(ErrorMessage) ? "failed" : "success";
}
