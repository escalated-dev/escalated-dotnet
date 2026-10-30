using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Escalated.Authorization;
using Escalated.Configuration;
using Escalated.Data;
using Escalated.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OtpNet;

namespace Escalated.Services;

/// <summary>
/// Two-factor authentication (ADR 2026-10-29-configurable-access-features): TOTP as RFC 6238
/// (SHA-1, 6 digits, 30 s) through Otp.NET, one step of drift either side, each time step accepted
/// once; eight single-use recovery codes. The secret and the recovery codes are encrypted with
/// ASP.NET Core Data Protection.
/// </summary>
public class TwoFactorService
{
    /// <summary>Steps of clock drift accepted either side of now.</summary>
    public const int Window = 1;

    private const string Purpose = "Escalated.TwoFactor";

    private readonly EscalatedDbContext _db;
    private readonly AccessFeatures _features;
    private readonly SettingsService _settings;
    private readonly EscalatedOptions _options;
    private readonly IDataProtector _protector;

    public TwoFactorService(EscalatedDbContext db, AccessFeatures features, SettingsService settings,
        IOptions<EscalatedOptions> options, IDataProtectionProvider protection)
    {
        _db = db;
        _features = features;
        _settings = settings;
        _options = options.Value;
        _protector = protection.CreateProtector(Purpose);
    }

    public bool Enabled => _features.TwoFactor;

    /// <summary>
    /// Whether staff must enrol: the <c>two_factor_required</c> setting when an admin has saved it,
    /// else <c>Escalated:TwoFactor:Required</c> / <c>ESCALATED_TWO_FACTOR_REQUIRED</c>. Only while on.
    /// </summary>
    public async Task<bool> RequiredAsync(CancellationToken ct = default)
    {
        if (!Enabled)
        {
            return false;
        }

        var stored = await _db.Settings.AsNoTracking().Where(s => s.Key == "two_factor_required").Select(s => s.Value).FirstOrDefaultAsync(ct);
        if (!string.IsNullOrWhiteSpace(stored))
        {
            return AccessFeatures.Truthy(stored);
        }

        return _options.TwoFactor.Required ?? AccessFeatures.Truthy(Environment.GetEnvironmentVariable("ESCALATED_TWO_FACTOR_REQUIRED"));
    }

    public Task<TwoFactor?> EnrolmentAsync(string? userId, CancellationToken ct = default) =>
        userId is null ? Task.FromResult<TwoFactor?>(null) : _db.TwoFactors.FirstOrDefaultAsync(t => t.UserId == userId, ct);

    public async Task<TwoFactor?> ConfirmedForAsync(string? userId, CancellationToken ct = default)
    {
        var enrolment = await EnrolmentAsync(userId, ct);
        return enrolment?.IsConfirmed == true ? enrolment : null;
    }

    public sealed record Setup(string QrUri, IReadOnlyList<string> RecoveryCodes);

    /// <summary>Replaces any pending enrolment of <paramref name="userId"/> with a new one.</summary>
    public async Task<Setup> SetupAsync(string userId, string? label, CancellationToken ct = default)
    {
        var secret = Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20)).TrimEnd('=');
        var codes = NewRecoveryCodes();
        var enrolment = await EnrolmentAsync(userId, ct);
        if (enrolment is null)
        {
            enrolment = new TwoFactor { UserId = userId };
            _db.TwoFactors.Add(enrolment);
        }

        enrolment.Secret = _protector.Protect(secret);
        enrolment.RecoveryCodes = _protector.Protect(JsonSerializer.Serialize(codes));
        enrolment.ConfirmedAt = null;
        enrolment.LastUsedStep = null;
        enrolment.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return new Setup(QrUri(secret, label ?? userId), codes);
    }

    /// <summary>Confirms a pending enrolment with a valid code: its recovery codes, or null.</summary>
    public async Task<IReadOnlyList<string>?> ConfirmAsync(string userId, string code, CancellationToken ct = default)
    {
        var enrolment = await EnrolmentAsync(userId, ct);
        if (enrolment is null || enrolment.IsConfirmed || !await VerifyAndConsumeAsync(enrolment, code, ct))
        {
            return null;
        }

        enrolment.ConfirmedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return RecoveryCodesOf(enrolment);
    }

    public async Task DisableAsync(string userId, CancellationToken ct = default)
    {
        await _db.TwoFactors.Where(t => t.UserId == userId).ExecuteDeleteAsync(ct);
    }

    /// <summary>Clears a user's enrolment (a lost device), as Laravel's <c>escalated:two-factor:reset</c>.</summary>
    public Task ResetAsync(string userId, CancellationToken ct = default) => DisableAsync(userId, ct);

    /// <summary>
    /// Whether <paramref name="code"/> is valid now, recording its time step so that step and every
    /// earlier one are refused afterwards (a code works once).
    /// </summary>
    public async Task<bool> VerifyAndConsumeAsync(TwoFactor enrolment, string? code, CancellationToken ct = default)
    {
        var digits = new string((code ?? string.Empty).Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (digits.Length != 6 || !digits.All(char.IsAsciiDigit))
        {
            return false;
        }

        byte[] key;
        try
        {
            key = Base32Encoding.ToBytes(_protector.Unprotect(enrolment.Secret));
        }
        catch (CryptographicException)
        {
            return false;
        }

        var totp = new Totp(key, step: 30, mode: OtpHashMode.Sha1, totpSize: 6);
        if (!totp.VerifyTotp(digits, out var step, new VerificationWindow(Window, Window)))
        {
            return false;
        }

        if (enrolment.LastUsedStep is { } last && step <= last)
        {
            return false;
        }

        // Conditional, so two requests with the same code cannot both pass.
        var updated = await _db.TwoFactors
            .Where(t => t.Id == enrolment.Id && (t.LastUsedStep == null || t.LastUsedStep < step))
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.LastUsedStep, step), ct);
        if (updated != 1)
        {
            return false;
        }

        enrolment.LastUsedStep = step;
        return true;
    }

    /// <summary>Uses a recovery code: true, and the code is gone, when it matches one.</summary>
    public async Task<bool> UseRecoveryCodeAsync(TwoFactor enrolment, string? code, CancellationToken ct = default)
    {
        var wanted = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (wanted.Length == 0)
        {
            return false;
        }

        List<string> codes;
        try
        {
            codes = RecoveryCodesOf(enrolment).ToList();
        }
        catch (CryptographicException)
        {
            return false;
        }

        var match = codes.FirstOrDefault(candidate => CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(candidate.ToUpperInvariant()), Encoding.UTF8.GetBytes(wanted)));
        if (match is null)
        {
            return false;
        }

        codes.Remove(match);
        enrolment.RecoveryCodes = _protector.Protect(JsonSerializer.Serialize(codes));
        enrolment.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>The otpauth:// URI an authenticator app reads from a QR code.</summary>
    public string QrUri(string secret, string label)
    {
        var issuer = _options.TwoFactor.Issuer;
        return $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(label)}?secret={secret}"
            + $"&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits=6&period=30";
    }

    /// <summary>The base32 secret of an enrolment; for tests and tools.</summary>
    public string SecretOf(TwoFactor enrolment) => _protector.Unprotect(enrolment.Secret);

    /// <summary>The code for a base32 secret at <paramref name="when"/>; for tests and tools.</summary>
    public static string CodeAt(string secret, DateTime when) =>
        new Totp(Base32Encoding.ToBytes(secret), step: 30, mode: OtpHashMode.Sha1, totpSize: 6).ComputeTotp(when);

    private IReadOnlyList<string> RecoveryCodesOf(TwoFactor enrolment) =>
        enrolment.RecoveryCodes is null
            ? Array.Empty<string>()
            : JsonSerializer.Deserialize<List<string>>(_protector.Unprotect(enrolment.RecoveryCodes)) ?? new List<string>();

    private static List<string> NewRecoveryCodes() => Enumerable.Range(0, 8)
        .Select(_ => Convert.ToHexString(RandomNumberGenerator.GetBytes(4)) + "-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4)))
        .ToList();
}
