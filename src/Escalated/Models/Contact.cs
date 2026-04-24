using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Escalated.Models;

/// <summary>
/// First-class identity for guest requesters. Deduped by email
/// (unique index, case-insensitively normalized on write). Links to
/// a host-app user via <see cref="UserId"/> once the guest accepts a
/// signup invite.
///
/// Coexists with the inline Guest* columns on <see cref="Ticket"/>
/// for one release — a follow-up migration backfills
/// <see cref="Ticket.ContactId"/> from <see cref="Ticket.GuestEmail"/>.
/// New code should resolve contacts via
/// <see cref="FindOrCreateByEmail"/>.
/// </summary>
[Table("escalated_contacts")]
public class Contact
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(320)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(255)]
    public string? Name { get; set; }

    /// <summary>Linked host-app user id once the contact creates an account.</summary>
    public int? UserId { get; set; }

    /// <summary>
    /// Free-form metadata as JSON-serialized string. Callers usually
    /// round-trip through <see cref="GetMetadata"/> / <see cref="SetMetadata"/>.
    /// </summary>
    public string Metadata { get; set; } = "{}";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Dictionary<string, object?> GetMetadata() =>
        string.IsNullOrWhiteSpace(Metadata)
            ? new()
            : JsonSerializer.Deserialize<Dictionary<string, object?>>(Metadata) ?? new();

    public void SetMetadata(Dictionary<string, object?> value) =>
        Metadata = JsonSerializer.Serialize(value);

    /// <summary>
    /// Canonical email normalization: trim whitespace, lowercase.
    /// Called from <see cref="FindOrCreateByEmail"/> and should be
    /// called on any caller-supplied email before lookups.
    /// </summary>
    public static string NormalizeEmail(string? email) =>
        (email ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>
    /// Returns 'create' when no existing contact matched,
    /// 'update-name' when the existing row has a blank name and
    /// a non-blank name was provided, or 'return-existing' otherwise.
    /// Pure function — testable without a database.
    /// </summary>
    public static string DecideAction(Contact? existing, string? incomingName)
    {
        if (existing is null) return "create";
        if (string.IsNullOrEmpty(existing.Name) && !string.IsNullOrEmpty(incomingName))
            return "update-name";
        return "return-existing";
    }
}
