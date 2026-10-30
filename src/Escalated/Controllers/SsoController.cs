using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Escalated.Authorization;
using Escalated.Configuration;
using Escalated.Data;
using Escalated.Models;
using Escalated.Services;
using Escalated.Ui;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Escalated.Controllers;

/// <summary>
/// The SSO sign-in routes (ADR 2026-10-29-configurable-access-features), all under
/// <c>{prefix}/sso</c>, open to guests, answering 404 while SSO is off (<see cref="EnsureAccessFeaturesFilter"/>)
/// and for any provider other than the one chosen in Admin &gt; Settings &gt; SSO.
///
/// <para>A successful sign-in finds (or, with <c>Sso.CreateUsers</c>, creates) the host user by email,
/// signs them in through <see cref="SsoOptions.SignIn"/> (default: the default sign-in scheme), clears
/// the two-factor mark (SSO does not skip the challenge), optionally maps the role, and redirects to
/// <see cref="SsoService.RedirectTo"/>. Every refusal answers a generic 403 and logs the reason.</para>
/// </summary>
[ApiController]
[Route("support/sso")]
[Microsoft.AspNetCore.Authorization.AllowAnonymous]
public class SsoController : ControllerBase
{
    internal const string OAuthCookie = "escalated_sso_oauth";
    private const string OAuthPurpose = "Escalated.Sso.OAuth";

    private readonly SsoService _sso;
    private readonly EscalatedDbContext _db;
    private readonly AccessFeatures _features;
    private readonly IUserDirectory _users;
    private readonly EscalatedOptions _options;
    private readonly IDataProtectionProvider _protection;
    private readonly ILogger<SsoController> _logger;

    public SsoController(SsoService sso, EscalatedDbContext db, AccessFeatures features, IUserDirectory users,
        IOptions<EscalatedOptions> options, IDataProtectionProvider protection, ILogger<SsoController> logger)
    {
        _sso = sso;
        _db = db;
        _features = features;
        _users = users;
        _options = options.Value;
        _protection = protection;
        _logger = logger;
    }

    [HttpGet("login")]
    public async Task<IActionResult> Login(CancellationToken ct)
    {
        try
        {
            switch (await _sso.ProviderAsync(ct))
            {
                case "saml":
                    return Redirect(await _sso.SamlLoginUrlAsync(Request, ct));
                case "oauth":
                    var start = await _sso.OAuthStartAsync(Request, ct);
                    // state and the PKCE verifier, bound to this browser, single use.
                    Response.Cookies.Append(OAuthCookie, _protection.CreateProtector(OAuthPurpose).Protect(start.State + "|" + start.Verifier),
                        new CookieOptions { Path = "/", HttpOnly = true, SameSite = SameSiteMode.Lax, Secure = Request.IsHttps, MaxAge = TimeSpan.FromMinutes(10) });
                    return Redirect(start.Url);
                case "jwt":
                    return Redirect(await _sso.JwtLoginUrlAsync(Request, ct));
                default:
                    return NotFound();
            }
        }
        catch (SsoException refused)
        {
            return Refuse(refused);
        }
    }

    [HttpGet("saml/metadata")]
    public async Task<IActionResult> Metadata(CancellationToken ct) =>
        await _sso.ProviderAsync(ct) != "saml" ? NotFound() : Content(_sso.SamlMetadata(Request), "application/xml");

    [HttpPost("saml/acs")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Acs([FromForm(Name = "SAMLResponse")] string? samlResponse, CancellationToken ct)
    {
        if (await _sso.ProviderAsync(ct) != "saml")
        {
            return NotFound();
        }

        try
        {
            return await SignInAsync(await _sso.ValidateSamlResponseAsync(Request, samlResponse, ct), ct);
        }
        catch (SsoException refused)
        {
            return Refuse(refused);
        }
    }

    [HttpGet("oauth/callback")]
    public async Task<IActionResult> OAuthCallback([FromQuery] string? state, [FromQuery] string? code, [FromQuery] string? error,
        CancellationToken ct)
    {
        if (await _sso.ProviderAsync(ct) != "oauth")
        {
            return NotFound();
        }

        string? expected = null;
        string? verifier = null;
        if (Request.Cookies.TryGetValue(OAuthCookie, out var cookie))
        {
            try
            {
                var parts = _protection.CreateProtector(OAuthPurpose).Unprotect(cookie).Split('|', 2);
                (expected, verifier) = (parts[0], parts.Length > 1 ? parts[1] : null);
            }
            catch (CryptographicException)
            {
                // A forged or stale cookie is no state at all.
            }
        }

        // Single use, whatever happens next.
        Response.Cookies.Delete(OAuthCookie, new CookieOptions { Path = "/" });
        try
        {
            if (!string.IsNullOrEmpty(error))
            {
                throw new SsoException($"OAuth refused: the provider answered {error}.");
            }

            if (state is null || expected is null
                || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(expected)))
            {
                throw new SsoException("OAuth refused: missing or mismatched state.");
            }

            if (string.IsNullOrEmpty(code) || verifier is null)
            {
                throw new SsoException("OAuth refused: no authorization code.");
            }

            return await SignInAsync(await _sso.OAuthIdentityAsync(Request, code, verifier, ct), ct);
        }
        catch (SsoException refused)
        {
            return Refuse(refused);
        }
    }

    [HttpGet("jwt/callback")]
    [HttpPost("jwt/callback")]
    public async Task<IActionResult> JwtCallback(CancellationToken ct)
    {
        if (await _sso.ProviderAsync(ct) != "jwt")
        {
            return NotFound();
        }

        string? token = Request.Query["jwt"];
        if (string.IsNullOrEmpty(token) && Request.HasFormContentType)
        {
            token = (await Request.ReadFormAsync(ct))["jwt"];
        }

        try
        {
            return await SignInAsync(await _sso.ValidateJwtAsync(Request, token, ct), ct);
        }
        catch (SsoException refused)
        {
            return Refuse(refused);
        }
    }

    private async Task<IActionResult> SignInAsync(SsoIdentity identity, CancellationToken ct)
    {
        var userId = await FindAsync(identity.Email, ct);
        if (userId is null)
        {
            if (!_sso.CreateUsers)
            {
                throw new SsoException($"SSO refused: no user with email {identity.Email} (CreateUsers is off).");
            }

            userId = _options.Sso.CreateUser is null
                ? throw new SsoException("SSO refused: this host cannot create users (no Sso.CreateUser).")
                : await _options.Sso.CreateUser(identity.Email, identity.Name, ct)
                    ?? throw new SsoException("SSO refused: the host did not create the user.");
        }

        await SyncRoleAsync(userId, identity.Role, ct);

        if (_options.Sso.SignIn is not null)
        {
            await _options.Sso.SignIn(HttpContext, userId);
        }
        else
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Email, identity.Email),
                new Claim(ClaimTypes.Name, identity.Name.Length > 0 ? identity.Name : identity.Email),
            }, "Escalated.Sso"));
            await HttpContext.SignInAsync(principal);
        }

        // A new login: the two-factor challenge comes again.
        TwoFactorSession.Forget(HttpContext);
        return Redirect(_sso.RedirectTo);
    }

    private async Task<string?> FindAsync(string email, CancellationToken ct)
    {
        if (_options.Sso.FindUserByEmail is not null)
        {
            return await _options.Sso.FindUserByEmail(email, ct);
        }

        var page = await _users.ListAsync(email, 1, 50, ct);
        return page.Items.FirstOrDefault(u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    /// <summary>With roles and permissions on and SyncRoles on, a role naming an Escalated role replaces the user's.</summary>
    private async Task SyncRoleAsync(string userId, string role, CancellationToken ct)
    {
        if (!_features.RolesPermissions || !_sso.SyncRoles || string.IsNullOrWhiteSpace(role))
        {
            return;
        }

        var found = await _db.Roles.FirstOrDefaultAsync(r => r.Slug == role.Trim(), ct);
        if (found is null || found.Slug is "escalated-admin" or "escalated-agent")
        {
            // Unknown roles change nothing, and SSO never grants the admin or agent gate.
            return;
        }

        var gates = await _db.Roles.Where(r => r.Slug == "escalated-admin" || r.Slug == "escalated-agent").Select(r => r.Id).ToListAsync(ct);
        var held = await _db.RoleUsers.Where(ru => ru.UserId == userId && !gates.Contains(ru.RoleId)).ToListAsync(ct);
        _db.RoleUsers.RemoveRange(held);
        _db.RoleUsers.Add(new RoleUser { RoleId = found.Id, UserId = userId });
        await _db.SaveChangesAsync(ct);
    }

    private IActionResult Refuse(SsoException reason)
    {
        _logger.LogWarning("Escalated SSO sign-in refused: {Reason}", reason.Message);
        return new ContentResult { StatusCode = 403, Content = "Single sign-on failed.", ContentType = "text/plain" };
    }
}
