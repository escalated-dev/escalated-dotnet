using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Escalated.Models;

/// <summary>
/// One emailed guest verification code. Neither the code nor the email is
/// stored in the clear: both are HMAC digests (see <c>GuestAccessService</c>).
/// A code is bound to the email and the purpose, lasts ten minutes, allows five
/// guesses and can be spent once.
/// </summary>
public class GuestVerification
{
    /// <summary>The public <c>verification_id</c> (a GUID).</summary>
    [Key]
    [MaxLength(36)]
    public string Id { get; set; } = string.Empty;

    /// <summary><c>ticket</c>, <c>chat</c> or <c>lookup</c>.</summary>
    [MaxLength(16)]
    public string Purpose { get; set; } = string.Empty;

    [Column("email_digest")]
    [MaxLength(64)]
    public string EmailDigest { get; set; } = string.Empty;

    [Column("client_digest")]
    [MaxLength(64)]
    public string ClientDigest { get; set; } = string.Empty;

    [Column("code_digest")]
    [MaxLength(64)]
    public string CodeDigest { get; set; } = string.Empty;

    public int Attempts { get; set; }

    [Column("expires_at")]
    public DateTime ExpiresAt { get; set; }

    [Column("consumed_at")]
    public DateTime? ConsumedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
