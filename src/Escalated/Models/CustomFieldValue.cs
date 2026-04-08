using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Escalated.Models;

public class CustomFieldValue
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int CustomFieldId { get; set; }

    // Polymorphic entity
    [MaxLength(255)]
    public string EntityType { get; set; } = string.Empty;
    public int EntityId { get; set; }

    public string? Value { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(CustomFieldId))]
    public CustomField? CustomField { get; set; }
}
