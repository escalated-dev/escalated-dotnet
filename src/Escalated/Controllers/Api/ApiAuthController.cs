using Escalated.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Escalated.Controllers.Api;

/// <summary>
/// General JSON API authentication for the Flutter app and integrations. All
/// credential handling is delegated to host-app callbacks
/// (<see cref="ApiAuthOptions"/>) — Escalated owns no passwords or sessions.
/// An unconfigured callback responds 501; a callback returning <c>null</c> is
/// an authentication failure (401).
///
/// login/register/refresh/logout carry no token yet; the host should leave
/// those paths unauthenticated when wiring <c>ApiTokenAuthMiddleware</c>.
/// </summary>
[ApiController]
[Route("support/api/v1/auth")]
public class ApiAuthController : ControllerBase
{
    private readonly ApiAuthOptions _auth;

    public ApiAuthController(IOptions<EscalatedOptions> options)
    {
        _auth = options.Value.ApiAuth;
    }

    [HttpPost("login")]
    public Task<IActionResult> Login([FromBody] Dictionary<string, object?>? body) =>
        DelegateAsync(_auth.Authenticate, body ?? new Dictionary<string, object?>());

    [HttpPost("register")]
    public Task<IActionResult> Register([FromBody] Dictionary<string, object?>? body) =>
        DelegateAsync(_auth.Register, body ?? new Dictionary<string, object?>());

    [HttpPost("refresh")]
    public Task<IActionResult> Refresh() =>
        DelegateAsync(_auth.Refresh, BearerToken());

    [HttpGet("me")]
    public Task<IActionResult> Me() =>
        DelegateAsync(_auth.Validate, BearerToken());

    [HttpPatch("profile")]
    public async Task<IActionResult> Profile([FromBody] Dictionary<string, object?>? body)
    {
        if (_auth.UpdateProfile is null)
        {
            return NotConfigured();
        }

        var result = await _auth.UpdateProfile(BearerToken(), body ?? new Dictionary<string, object?>());
        return result is null
            ? Unauthorized(new { error = "Unauthorized" })
            : Ok(new { data = result });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        if (_auth.Logout is not null)
        {
            await _auth.Logout(BearerToken());
        }

        return Ok(new { data = new { success = true } });
    }

    [HttpPost("validate")]
    public async Task<IActionResult> Validate([FromBody] Dictionary<string, object?>? body)
    {
        var token = body is not null && body.TryGetValue("token", out var value) ? value?.ToString() : null;
        if (string.IsNullOrEmpty(token))
        {
            return BadRequest(new { error = "token is required" });
        }

        if (_auth.Validate is null)
        {
            return NotConfigured();
        }

        var user = await _auth.Validate(token);
        return user is null
            ? Unauthorized(new { error = "Invalid token" })
            : Ok(new { valid = true, user });
    }

    private async Task<IActionResult> DelegateAsync<T>(
        Func<T, Task<Dictionary<string, object?>?>>? callback,
        T arg)
    {
        if (callback is null)
        {
            return NotConfigured();
        }

        var result = await callback(arg);
        return result is null
            ? Unauthorized(new { error = "Unauthorized" })
            : Ok(new { data = result });
    }

    private string BearerToken()
    {
        var header = Request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.Ordinal)
            ? header["Bearer ".Length..].Trim()
            : string.Empty;
    }

    private IActionResult NotConfigured() =>
        StatusCode(StatusCodes.Status501NotImplemented, new { error = "Authentication is not configured" });
}
