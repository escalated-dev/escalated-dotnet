namespace Escalated.Models;

/// <summary>A host user assigned to a support department, without a cross-database user FK.</summary>
public class DepartmentAgent
{
    public int DepartmentId { get; set; }
    public string AgentId { get; set; } = string.Empty;
    [System.Text.Json.Serialization.JsonIgnore]
    public Department? Department { get; set; }
}
