using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Escalated.Models;

public class Holiday
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int ScheduleId { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public DateTime Date { get; set; }

    public bool Recurring { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(ScheduleId))]
    public BusinessSchedule? Schedule { get; set; }
}
