using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace Escalated.Models;

public class Skill
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Slug { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<AgentSkill> AgentSkills { get; set; } = new List<AgentSkill>();

    public ICollection<SkillRoutingTag> RoutingTags { get; set; } = new List<SkillRoutingTag>();

    public ICollection<SkillRoutingDepartment> RoutingDepartments { get; set; } = new List<SkillRoutingDepartment>();

    public static string GenerateSlug(string name)
    {
        var slug = name.ToLowerInvariant();
        slug = Regex.Replace(slug, @"[^a-z0-9\s-]", "");
        slug = Regex.Replace(slug, @"\s+", "-");
        return slug.Trim('-');
    }
}

public class AgentSkill
{
    [Key]
    public int Id { get; set; }

    public int UserId { get; set; }
    public int SkillId { get; set; }

    /// <summary>1 (beginner) .. 5 (expert).</summary>
    [Range(1, 5)]
    public int Proficiency { get; set; } = 3;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Skill? Skill { get; set; }
}

public class SkillRoutingTag
{
    [Key]
    public int Id { get; set; }

    public int SkillId { get; set; }
    public Skill? Skill { get; set; }

    public int TagId { get; set; }
    public Tag? Tag { get; set; }
}

public class SkillRoutingDepartment
{
    [Key]
    public int Id { get; set; }

    public int SkillId { get; set; }
    public Skill? Skill { get; set; }

    public int DepartmentId { get; set; }
    public Department? Department { get; set; }
}
