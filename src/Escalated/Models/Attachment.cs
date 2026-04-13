using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Escalated.Models;

public class Attachment
{
    [Key]
    public int Id { get; set; }

    // Polymorphic: can belong to Ticket or Reply
    [MaxLength(255)]
    public string AttachableType { get; set; } = string.Empty;
    public int AttachableId { get; set; }

    [Required]
    [MaxLength(255)]
    public string Filename { get; set; } = string.Empty;

    [MaxLength(100)]
    public string MimeType { get; set; } = "application/octet-stream";

    public long Size { get; set; }

    [MaxLength(1024)]
    public string Path { get; set; } = string.Empty;

    [MaxLength(50)]
    public string Disk { get; set; } = "local";

    /// <summary>
    /// Download URL for this attachment. Populated at serialization time, not persisted.
    /// </summary>
    [NotMapped]
    public string? Url { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public string SizeForHumans()
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = Size;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}
