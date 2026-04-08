using System.ComponentModel.DataAnnotations;
using Escalated.Enums;

namespace Escalated.Models;

public class SlaPolicy
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>
    /// JSON: per-priority first response hours, e.g. {"low":24,"medium":8,"high":4,"urgent":1,"critical":0.5}
    /// </summary>
    public string? FirstResponseHours { get; set; }

    /// <summary>
    /// JSON: per-priority resolution hours
    /// </summary>
    public string? ResolutionHours { get; set; }

    public bool BusinessHoursOnly { get; set; }

    public bool IsDefault { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Ticket> Tickets { get; set; } = new List<Ticket>();

    public double? GetFirstResponseHoursFor(Enums.TicketPriority priority)
    {
        return GetHoursFromJson(FirstResponseHours, priority.ToValue());
    }

    public double? GetResolutionHoursFor(Enums.TicketPriority priority)
    {
        return GetHoursFromJson(ResolutionHours, priority.ToValue());
    }

    private static double? GetHoursFromJson(string? json, string key)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, double>>(json);
            if (dict != null && dict.TryGetValue(key, out var hours))
                return hours;
        }
        catch { }
        return null;
    }
}
