using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Escalated.Models;

/// <summary>
/// The rest of a Workflow's actions, held back by a <c>delay</c> action until
/// <see cref="ExecuteAt"/>. <see cref="Services.WorkflowDelayedActionProcessor"/>
/// runs it when due, after re-checking the Workflow's conditions; closing or
/// resolving the ticket first cancels it. Mirrors Laravel's <c>DelayedAction</c>.
/// </summary>
public class WorkflowDelayedAction
{
    [Key]
    public int Id { get; set; }

    [Column("workflow_id")]
    public int WorkflowId { get; set; }

    [Column("ticket_id")]
    public int TicketId { get; set; }

    /// <summary>The <c>delay</c> action itself, as JSON.</summary>
    public string Action { get; set; } = "{}";

    /// <summary>The actions that follow the delay, as a JSON array.</summary>
    [Column("remaining_actions")]
    public string RemainingActions { get; set; } = "[]";

    [Column("execute_at")]
    public DateTime ExecuteAt { get; set; }

    public bool Executed { get; set; }

    public bool Cancelled { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
