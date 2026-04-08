using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace Escalated.Models;

public class CustomField
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Slug { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string Type { get; set; } = "text"; // text, number, select, checkbox, date, textarea

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

    public ICollection<CustomFieldValue> Values { get; set; } = new List<CustomFieldValue>();
}
