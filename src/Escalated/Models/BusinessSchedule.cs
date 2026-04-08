using System.ComponentModel.DataAnnotations;

namespace Escalated.Models;

public class BusinessSchedule
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(50)]
    public string Timezone { get; set; } = "UTC";

    /// <summary>
    /// JSON: day-of-week schedule, e.g. {"monday":{"start":"09:00","end":"17:00"}, ...}
    /// </summary>
    public string? Schedule { get; set; }

    public bool IsDefault { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Holiday> Holidays { get; set; } = new List<Holiday>();
}
