using System.ComponentModel.DataAnnotations;

namespace Escalated.Models;

/// <summary>
/// An SSO message ID this install issued or accepted (<c>escalated_sso_message_ids</c>, ADR
/// 2026-10-29-configurable-access-features): a pending SAML AuthnRequest (<c>saml_request</c>), an
/// accepted assertion (<c>saml_assertion</c>) or a JWT <c>jti</c> (<c>jwt</c>), kept until
/// <see cref="ExpiresAt"/> (Unix seconds).
/// </summary>
public class SsoMessageId
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(32)]
    public string Kind { get; set; } = string.Empty;

    [Required]
    [MaxLength(191)]
    public string MessageId { get; set; } = string.Empty;

    public long ExpiresAt { get; set; }
}
