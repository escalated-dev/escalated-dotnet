using System.ComponentModel.DataAnnotations;

namespace Escalated.Models;

public class CustomObject
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// JSON schema for the object's fields
    /// </summary>
    public string? FieldsSchema { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<CustomObjectRecord> Records { get; set; } = new List<CustomObjectRecord>();
}

public class CustomObjectRecord
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int ObjectId { get; set; }

    public string? Data { get; set; } // JSON

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public CustomObject? Object { get; set; }
}
