using System.ComponentModel.DataAnnotations;

namespace Escalated.Models;

public class ApiToken
{
    [Key]
    public int Id { get; set; }

    [MaxLength(255)]
    public string? TokenableType { get; set; }
    public int? TokenableId { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(64)]
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>
    /// JSON array of abilities, e.g. ["tickets:read","tickets:write"]
    /// </summary>
    public string? Abilities { get; set; }

    public DateTime? LastUsedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public bool IsExpired => ExpiresAt.HasValue && ExpiresAt.Value < DateTime.UtcNow;

    public bool HasAbility(string ability)
    {
        if (string.IsNullOrEmpty(Abilities)) return false;
        try
        {
            var abilities = System.Text.Json.JsonSerializer.Deserialize<List<string>>(Abilities);
            return abilities != null && (abilities.Contains("*") || abilities.Contains(ability));
        }
        catch
        {
            return false;
        }
    }
}
