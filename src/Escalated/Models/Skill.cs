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
    public int UserId { get; set; }
    public int SkillId { get; set; }

    [MaxLength(50)]
    public string? Proficiency { get; set; } // beginner, intermediate, expert

    public Skill? Skill { get; set; }
}
