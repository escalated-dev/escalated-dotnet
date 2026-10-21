using System.Linq.Expressions;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Escalated.Configuration;
using Escalated.Data;
using Escalated.Models;
using Escalated.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Escalated.Services;

/// <summary>
/// Verified guest access, as escalated-laravel's <c>GuestVerification</c> and
/// <c>GuestAccess</c> (<c>docs/guest-access.md</c>):
/// <list type="bullet">
///   <item>An eight-digit code is emailed to prove the guest owns the mailbox. It is
///   bound to the normalized email and a purpose (<c>ticket</c>, <c>chat</c>,
///   <c>lookup</c>), lasts ten minutes, allows five guesses (each one counts) and is
///   spent once. Codes per mailbox are budgeted per client IP and overall.</item>
///   <item>A verified guest gets an opaque, expiring access grant for their ticket.
///   Only its SHA-256 is stored; issuing a new grant revokes the previous one.</item>
/// </list>
/// Emails and codes are stored as HMAC digests under <see cref="GuestAccessOptions.Secret"/>.
/// </summary>
public class GuestAccessService
{
    public const int CodeLifetimeSeconds = 600;
    public const int MaxAttempts = 5;
    public static readonly string[] Purposes = { "ticket", "chat", "lookup" };

    private static readonly byte[] ProcessKey = RandomNumberGenerator.GetBytes(32);
    private static int _warnedProcessKey;

    private readonly EscalatedDbContext _db;
    private readonly EscalatedOptions _options;
    private readonly IEscalatedMailTransport _mail;
    private readonly TimeProvider _time;
    private readonly ILogger<GuestAccessService> _logger;

    public GuestAccessService(
        EscalatedDbContext db,
        IOptions<EscalatedOptions> options,
        IEscalatedMailTransport mail,
        ILogger<GuestAccessService> logger,
        TimeProvider? time = null)
    {
        _db = db;
        _options = options.Value;
        _mail = mail;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    /// <summary>Trimmed and lower-cased, or null when it is not an email address.</summary>
    public static string? NormalizeEmail(string? email)
    {
        var trimmed = email?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > 255) return null;
        try
        {
            var address = new MailAddress(trimmed);
            return address.Address == trimmed && trimmed.Contains('@') ? trimmed.ToLowerInvariant() : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>The outcome of a code request.</summary>
    public sealed record CodeRequest(string? VerificationId, string? Code, int RetryAfterSeconds)
    {
        public bool Throttled => VerificationId is null;
    }

    /// <summary>
    /// Makes a code for <paramref name="email"/> and <paramref name="purpose"/> and
    /// emails it. Returns the verification id, or a retry-after when the mailbox's
    /// budget is spent.
    /// </summary>
    public async Task<CodeRequest> RequestCodeAsync(string email, string purpose, string? clientIp, CancellationToken ct = default)
    {
        var request = await IssueCodeAsync(email, purpose, clientIp, ct);
        if (request.Throttled) return request;

        if (!_mail.IsConfigured)
        {
            _logger.LogWarning("A guest verification code was requested, but no mail transport is configured to send it (Escalated:Email:Smtp:Host).");
        }
        else
        {
            try
            {
                await _mail.SendAsync(new EmailMessage
                {
                    ToEmail = email.Trim(),
                    Subject = "Your verification code",
                    Body = $"<p>Your verification code is <strong>{request.Code}</strong>.</p><p>It expires in 10 minutes. If you did not ask for it, ignore this email.</p>",
                }, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Sending a guest verification code failed");
            }
        }

        return request with { Code = null };
    }

    /// <summary>
    /// Makes and stores a code without sending it, for hosts that deliver codes
    /// themselves. Returns the code; only its digest is stored.
    /// </summary>
    public async Task<CodeRequest> IssueCodeAsync(string email, string purpose, string? clientIp, CancellationToken ct = default)
    {
        var normalized = NormalizeEmail(email) ?? throw new ArgumentException("Not an email address.", nameof(email));
        if (!Purposes.Contains(purpose)) throw new ArgumentException("Unknown purpose.", nameof(purpose));

        var now = Now;
        var emailDigest = Digest("email", normalized);
        var clientDigest = Digest("client", clientIp ?? "unknown");
        var hourAgo = now.AddHours(-1);

        // Expired codes go as new ones are made.
        await _db.GuestVerifications.Where(v => v.ExpiresAt < hourAgo).ExecuteDeleteAsync(ct);

        var recent = await _db.GuestVerifications
            .Where(v => v.EmailDigest == emailDigest && v.CreatedAt > hourAgo)
            .Select(v => new { v.ClientDigest, v.CreatedAt })
            .ToListAsync(ct);
        var fromClient = recent.Where(v => v.ClientDigest == clientDigest).ToList();
        var access = _options.GuestAccess;
        if (fromClient.Count >= Math.Max(1, access.ChallengesPerClientPerHour)
            || recent.Count >= Math.Max(1, access.ChallengesPerMailboxPerHour))
        {
            var oldest = (fromClient.Count >= Math.Max(1, access.ChallengesPerClientPerHour) ? fromClient : recent).Min(v => v.CreatedAt);
            return new CodeRequest(null, null, Math.Max(1, (int)Math.Ceiling((oldest.AddHours(1) - now).TotalSeconds)));
        }

        var id = Guid.NewGuid().ToString();
        var code = RandomNumberGenerator.GetInt32(0, 100_000_000).ToString("D8", System.Globalization.CultureInfo.InvariantCulture);
        _db.GuestVerifications.Add(new GuestVerification
        {
            Id = id,
            Purpose = purpose,
            EmailDigest = emailDigest,
            ClientDigest = clientDigest,
            CodeDigest = Digest("code", $"{id}:{code}"),
            ExpiresAt = now.AddSeconds(CodeLifetimeSeconds),
            CreatedAt = now,
        });
        await _db.SaveChangesAsync(ct);

        return new CodeRequest(id, code, 0);
    }

    /// <summary>
    /// Spends a code: true when it exists, belongs to this email and purpose, is
    /// unexpired and unspent, has guesses left, and matches. Every guess counts.
    /// Call <see cref="ReleaseAsync"/> if the operation it authorized then fails.
    /// </summary>
    public async Task<bool> ConsumeAsync(string? verificationId, string? code, string? email, string purpose, CancellationToken ct = default)
    {
        var normalized = NormalizeEmail(email);
        if (normalized is null || string.IsNullOrEmpty(verificationId) || verificationId.Length > 36 || string.IsNullOrEmpty(code) || code.Length > 16)
            return false;

        var now = Now;
        var counted = await _db.GuestVerifications
            .Where(v => v.Id == verificationId && v.ConsumedAt == null && v.ExpiresAt > now && v.Attempts < MaxAttempts)
            .ExecuteUpdateAsync(set => set.SetProperty(v => v.Attempts, v => v.Attempts + 1), ct);
        if (counted == 0) return false;

        var row = await _db.GuestVerifications.AsNoTracking().FirstOrDefaultAsync(v => v.Id == verificationId, ct);
        if (row is null
            || row.Purpose != purpose
            || !FixedEquals(row.EmailDigest, Digest("email", normalized))
            || !FixedEquals(row.CodeDigest, Digest("code", $"{verificationId}:{code.Trim()}")))
        {
            return false;
        }

        // Claimed once: a concurrent request with the same code loses here.
        var claimed = await _db.GuestVerifications
            .Where(v => v.Id == verificationId && v.ConsumedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(v => v.ConsumedAt, now), ct);
        return claimed == 1;
    }

    /// <summary>Un-spends a code whose operation failed, so the guest can retry. Its guesses stay counted.</summary>
    public Task ReleaseAsync(string verificationId, CancellationToken ct = default) =>
        _db.GuestVerifications.Where(v => v.Id == verificationId)
            .ExecuteUpdateAsync(set => set.SetProperty(v => v.ConsumedAt, (DateTime?)null), ct);

    /// <summary>A grant's lifetime, clamped to 5 minutes..7 days.</summary>
    public TimeSpan GrantLifetime => TimeSpan.FromMinutes(Math.Clamp(_options.GuestAccess.TtlMinutes, 5, 7 * 24 * 60));

    /// <summary>
    /// Issues a new guest access grant for <paramref name="ticket"/>, revoking any
    /// earlier one. Returns the bearer token (shown once) and when it expires.
    /// A trusted entry point: callers must have verified the guest first.
    /// </summary>
    public async Task<(string Token, DateTime ExpiresAt)> IssueGrantAsync(Ticket ticket, CancellationToken ct = default)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var expires = Now.Add(GrantLifetime);
        ticket.GuestAccessHash = HashGrant(token);
        ticket.GuestAccessExpiresAt = expires;
        await _db.Tickets.IgnoreQueryFilters().Where(t => t.Id == ticket.Id)
            .ExecuteUpdateAsync(set => set
                .SetProperty(t => t.GuestAccessHash, ticket.GuestAccessHash)
                .SetProperty(t => t.GuestAccessExpiresAt, expires), ct);
        return (token, expires);
    }

    /// <summary>Revokes the ticket's guest access grant.</summary>
    public async Task RevokeAsync(Ticket ticket, CancellationToken ct = default)
    {
        ticket.GuestAccessHash = null;
        ticket.GuestAccessExpiresAt = null;
        await _db.Tickets.IgnoreQueryFilters().Where(t => t.Id == ticket.Id)
            .ExecuteUpdateAsync(set => set
                .SetProperty(t => t.GuestAccessHash, (string?)null)
                .SetProperty(t => t.GuestAccessExpiresAt, (DateTime?)null), ct);
    }

    /// <summary>
    /// Which tickets <paramref name="token"/> opens: the ticket whose unexpired grant
    /// it is, or, with <see cref="GuestAccessOptions.LegacyTokens"/>, the ticket whose
    /// permanent guest token it is.
    /// </summary>
    public Expression<Func<Ticket, bool>> Opens(string token) => Opens(token, _options.GuestAccess.LegacyTokens, Now);

    public static Expression<Func<Ticket, bool>> Opens(string token, bool legacyTokens, DateTime now)
    {
        var hash = HashGrant(token);
        return legacyTokens
            ? t => (t.GuestAccessHash == hash && t.GuestAccessExpiresAt > now) || t.GuestToken == token
            : t => t.GuestAccessHash == hash && t.GuestAccessExpiresAt > now;
    }

    public static string HashGrant(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private string Digest(string kind, string value)
    {
        using var hmac = new HMACSHA256(Key());
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{kind}:{value}"))).ToLowerInvariant();
    }

    private byte[] Key()
    {
        var secret = !string.IsNullOrEmpty(_options.GuestAccess.Secret) ? _options.GuestAccess.Secret : _options.Email.InboundSecret;
        if (!string.IsNullOrEmpty(secret)) return Encoding.UTF8.GetBytes(secret);

        if (Interlocked.Exchange(ref _warnedProcessKey, 1) == 0)
        {
            _logger.LogWarning("Escalated:GuestAccess:Secret and Escalated:Email:InboundSecret are both empty; guest verification uses a random key per process, so codes do not survive a restart or work across instances.");
        }

        return ProcessKey;
    }

    private static bool FixedEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a), Encoding.ASCII.GetBytes(b));
}
