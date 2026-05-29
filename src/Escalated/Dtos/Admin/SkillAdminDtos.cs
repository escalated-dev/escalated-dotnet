using System.ComponentModel.DataAnnotations;

namespace Escalated.Dtos.Admin;

public sealed class CreateSkillDto
{
    [Required]
    [MaxLength(100)]
    public required string Name { get; init; }

    [MaxLength(2000)]
    public string? Description { get; init; }

    public int[]? RoutingTagIds { get; init; }

    public int[]? RoutingDepartmentIds { get; init; }

    public AgentSkillEntryDto[]? Agents { get; init; }
}

public sealed class UpdateSkillDto
{
    [Required]
    [MaxLength(100)]
    public required string Name { get; init; }

    [MaxLength(2000)]
    public string? Description { get; init; }

    public int[]? RoutingTagIds { get; init; }

    public int[]? RoutingDepartmentIds { get; init; }

    public AgentSkillEntryDto[]? Agents { get; init; }
}

public sealed class AgentSkillEntryDto
{
    [Required]
    [MinLength(1)]
    [MaxLength(255)]
    public required string UserId { get; init; }

    [Range(1, 5)]
    public required int Proficiency { get; init; }
}
