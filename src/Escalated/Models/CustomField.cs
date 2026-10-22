using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;

namespace Escalated.Models;

public class CustomField
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(255)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Slug { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string Type { get; set; } = "text"; // text, textarea, select, multi_select, checkbox, date, number

    /// <summary>What the field describes: <c>ticket</c>, <c>user</c> or <c>organization</c>.</summary>
    [Required]
    [MaxLength(50)]
    public string Context { get; set; } = "ticket";

    [MaxLength(255)]
    public string? Placeholder { get; set; }

    public string? Description { get; set; }

    public string? Options { get; set; } // JSON array for select fields

    public string? ValidationRules { get; set; } // JSON

    /// <summary>
    /// JSON: conditional display logic, e.g. {"field":"priority","operator":"equals","value":"high"}
    /// </summary>
    public string? Conditions { get; set; }

    public bool Required { get; set; }

    public bool Active { get; set; } = true;

    public int Position { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public ICollection<CustomFieldValue> Values { get; set; } = new List<CustomFieldValue>();
}
