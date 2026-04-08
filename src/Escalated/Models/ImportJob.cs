using System.ComponentModel.DataAnnotations;

namespace Escalated.Models;

public class ImportJob
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(50)]
    public string Platform { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string Status { get; set; } = "pending";

    public string? Credentials { get; set; } // JSON, encrypted in practice

    public string? FieldMappings { get; set; } // JSON

    public string? Progress { get; set; } // JSON

    public string? Errors { get; set; } // JSON

    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ImportSourceMap> SourceMaps { get; set; } = new List<ImportSourceMap>();

    private static readonly Dictionary<string, string[]> ValidTransitions = new()
    {
        ["pending"] = new[] { "authenticating" },
        ["authenticating"] = new[] { "mapping", "failed" },
        ["mapping"] = new[] { "importing", "failed" },
        ["importing"] = new[] { "paused", "completed", "failed" },
        ["paused"] = new[] { "importing", "failed" },
        ["completed"] = Array.Empty<string>(),
        ["failed"] = new[] { "mapping" },
    };

    public bool CanTransitionTo(string newStatus)
    {
        return ValidTransitions.TryGetValue(Status, out var allowed)
            && allowed.Contains(newStatus);
    }

    public void TransitionTo(string newStatus)
    {
        if (!CanTransitionTo(newStatus))
            throw new InvalidOperationException($"Cannot transition from '{Status}' to '{newStatus}'.");
        Status = newStatus;
        UpdatedAt = DateTime.UtcNow;
    }
}
