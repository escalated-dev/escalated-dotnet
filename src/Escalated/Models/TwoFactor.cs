using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Escalated.Models;

/// <summary>
/// A staff user's TOTP enrolment (<c>escalated_two_factors</c>, ADR
/// 2026-10-29-configurable-access-features): pending until a code confirms it. The secret and
/// the recovery codes are stored encrypted with ASP.NET Core Data Protection and never serialised.
/// </summary>
public class TwoFactor
{
    [Key]
    public int Id { get; set; }

    /// <summary>The host user id; one enrolment each.</summary>
    [Required]
    [MaxLength(255)]
    public string UserId { get; set; } = string.Empty;

    [JsonIgnore]
    [Required]
    public string Secret { get; set; } = string.Empty;

    /// <summary>The unused recovery codes, a JSON array, encrypted.</summary>
    [JsonIgnore]
    public string? RecoveryCodes { get; set; }

    public DateTime? ConfirmedAt { get; set; }

    /// <summary>The time step of the last code accepted: that step and earlier ones are refused.</summary>
    public long? LastUsedStep { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public bool IsConfirmed => ConfirmedAt is not null;
}
