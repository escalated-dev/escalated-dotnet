using System.Collections.Concurrent;
using Escalated.Authorization;
using Escalated.Configuration;
using Escalated.Services;
using Escalated.Ui;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Escalated.Controllers.Ui;

/// <summary>
/// Two-factor enrolment and the challenge (ADR 2026-10-29-configurable-access-features),
/// escalated-laravel's <c>Admin\TwoFactorController</c> and <c>TwoFactorChallengeController</c>.
/// <list type="bullet">
/// <item><c>GET {prefix}/admin/settings/two-factor</c> renders <c>Admin/Settings/TwoFactor</c>
/// (<c>enabled</c>, <c>pending</c>, <c>required</c>) for every staff user, not only admins; the
/// <c>setup</c>, <c>confirm</c> and <c>disable</c> posts manage the user's own enrolment.</item>
/// <item><c>GET {prefix}/two-factor/challenge</c> renders <c>Auth/TwoFactorChallenge</c>
/// (<c>action</c>, <c>error</c>); posting <c>code</c> or <c>recovery_code</c> to the same path
/// passes it, 5 attempts per user per minute.</item>
/// </list>
/// Every route answers 404 while two-factor is off (<see cref="EnsureAccessFeaturesFilter"/>).
/// </summary>
[Authorize(Policy = EscalatedPolicies.Agent)]
[EscalatedPage]
public class TwoFactorPageController : ControllerBase
{
    private readonly TwoFactorThrottle _throttle;
    private readonly TwoFactorService _twoFactor;
    private readonly IAuthorizationService _authorization;
    private readonly EscalatedOptions _options;
    private readonly ILogger<TwoFactorPageController> _logger;

    public TwoFactorPageController(TwoFactorService twoFactor, IAuthorizationService authorization,
        IOptions<EscalatedOptions> options, ILogger<TwoFactorPageController> logger, TwoFactorThrottle throttle)
    {
        _throttle = throttle;
        _twoFactor = twoFactor;
        _authorization = authorization;
        _options = options.Value;
        _logger = logger;
    }

    private string Prefix => "/" + _options.RoutePrefix.Trim('/');
    private string EnrolmentPage => Prefix + "/admin/settings/two-factor";

    // --- Enrolment ---

    [HttpGet("support/admin/settings/two-factor")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var enrolment = await _twoFactor.EnrolmentAsync(this.CurrentUserId(), ct);
        return UiResults.Render("Escalated/Admin/Settings/TwoFactor", new
        {
            enabled = enrolment?.IsConfirmed ?? false,
            pending = enrolment is not null && !enrolment.IsConfirmed,
            required = await _twoFactor.RequiredAsync(ct),
        });
    }

    [HttpPost("support/admin/settings/two-factor/setup")]
    public async Task<IActionResult> Setup(CancellationToken ct)
    {
        var userId = this.CurrentUserId()!;
        if (await _twoFactor.ConfirmedForAsync(userId, ct) is not null)
        {
            return UiResults.Back(HttpContext, EnrolmentPage,
                error: "Two-factor authentication is already enabled. Disable it first to set it up again.");
        }

        var label = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? User.Identity?.Name ?? userId;
        var setup = await _twoFactor.SetupAsync(userId, label, ct);
        UiFlash.PutData(HttpContext, "two_factor_setup", new { qr_uri = setup.QrUri, recovery_codes = setup.RecoveryCodes });
        return UiResults.Back(HttpContext, EnrolmentPage);
    }

    [HttpPost("support/admin/settings/two-factor/confirm")]
    public async Task<IActionResult> Confirm(CancellationToken ct)
    {
        var userId = this.CurrentUserId()!;
        var input = await UiInput.ReadAsync(Request, ct);
        var code = input.String("code")?.Trim();
        if (string.IsNullOrEmpty(code) || code.Length != 6)
        {
            return Invalid("code", string.IsNullOrEmpty(code) ? "The code field is required." : "The code field must be 6 characters.");
        }

        var enrolment = await _twoFactor.EnrolmentAsync(userId, ct);
        if (enrolment is null || enrolment.IsConfirmed)
        {
            return Invalid("code", "No pending two-factor setup found.");
        }

        var codes = await _twoFactor.ConfirmAsync(userId, code, ct);
        if (codes is null)
        {
            return Invalid("code", "Invalid verification code.");
        }

        // The code just entered proves the second factor for this browser session.
        TwoFactorSession.MarkPassed(HttpContext, userId);
        UiFlash.PutData(HttpContext, "two_factor_confirmed", new { recovery_codes = codes });
        return UiResults.Back(HttpContext, EnrolmentPage, success: "Two-factor authentication enabled.");
    }

    [HttpPost("support/admin/settings/two-factor/disable")]
    public async Task<IActionResult> Disable(CancellationToken ct)
    {
        await _twoFactor.DisableAsync(this.CurrentUserId()!, ct);
        return UiResults.Back(HttpContext, EnrolmentPage, success: "Two-factor authentication disabled.");
    }

    // --- Challenge ---

    [HttpGet("support/two-factor/challenge")]
    public async Task<IActionResult> Challenge(CancellationToken ct)
    {
        var userId = this.CurrentUserId();
        if (await _twoFactor.ConfirmedForAsync(userId, ct) is null || TwoFactorSession.Passed(HttpContext, userId))
        {
            return new UiRedirectResult(await IntendedAsync());
        }

        return UiResults.Render("Escalated/Auth/TwoFactorChallenge", new
        {
            action = $"{Request.Scheme}://{Request.Host}{Request.PathBase}{Prefix}/two-factor/challenge",
            error = string.Empty,
        });
    }

    [HttpPost("support/two-factor/challenge")]
    public async Task<IActionResult> Verify(CancellationToken ct)
    {
        var userId = this.CurrentUserId()!;
        if (!_throttle.Allow(userId))
        {
            Response.Headers.RetryAfter = "60";
            return StatusCode(StatusCodes429);
        }

        var enrolment = await _twoFactor.ConfirmedForAsync(userId, ct);
        if (enrolment is null)
        {
            return new UiRedirectResult(await IntendedAsync());
        }

        var input = await UiInput.ReadAsync(Request, ct);
        var code = input.String("code");
        var recovery = input.String("recovery_code");
        if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(recovery))
        {
            return Invalid("code", "The code field is required when recovery code is not present.");
        }

        var usingRecovery = !string.IsNullOrWhiteSpace(recovery);
        if (usingRecovery ? recovery!.Length > 64 : code!.Length > 16)
        {
            return Invalid(usingRecovery ? "recovery_code" : "code", "The value is too long.");
        }

        var passed = usingRecovery
            ? await _twoFactor.UseRecoveryCodeAsync(enrolment, recovery, ct)
            : await _twoFactor.VerifyAndConsumeAsync(enrolment, code, ct);
        if (!passed)
        {
            _logger.LogInformation("Escalated: two-factor challenge failed for {UserId} from {Ip}", userId, HttpContext.Connection.RemoteIpAddress);
            return usingRecovery
                ? Invalid("recovery_code", "That recovery code is not valid.")
                : Invalid("code", "That code is not valid.");
        }

        TwoFactorSession.MarkPassed(HttpContext, userId);
        return new UiRedirectResult(await IntendedAsync());
    }

    private const int StatusCodes429 = 429;

    private async Task<string> IntendedAsync()
    {
        var intended = TwoFactorSession.TakeIntended(HttpContext);
        if (intended is not null)
        {
            return intended;
        }

        return (await _authorization.AuthorizeAsync(User, EscalatedPolicies.Agent)).Succeeded
            ? Prefix + "/agent"
            : Prefix + "/admin/reports";
    }

    private UiRedirectResult Invalid(string field, string message) =>
        UiResults.Back(HttpContext, Prefix + "/two-factor/challenge", errors: new Dictionary<string, string> { [field] = message });
}

/// <summary>The challenge's verify throttle: 5 attempts per user per minute.</summary>
public sealed class TwoFactorThrottle
{
    public const int AttemptsPerMinute = 5;

    private readonly ConcurrentDictionary<string, Queue<DateTime>> _attempts = new();

    public bool Allow(string userId)
    {
        var now = DateTime.UtcNow;
        var recent = _attempts.GetOrAdd(userId, _ => new Queue<DateTime>());
        lock (recent)
        {
            while (recent.Count > 0 && now - recent.Peek() > TimeSpan.FromMinutes(1))
            {
                recent.Dequeue();
            }

            if (recent.Count >= AttemptsPerMinute)
            {
                return false;
            }

            recent.Enqueue(now);
            return true;
        }
    }
}
