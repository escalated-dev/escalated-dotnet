using Escalated.Authorization;
using Escalated.Configuration;
using Escalated.Services;
using Escalated.Ui;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Escalated.Controllers.Ui;

/// <summary>
/// Admin &gt; Settings &gt; SSO (<c>Admin/Settings/SsoSettings</c>), escalated-laravel's
/// <c>SsoSettingsController</c>: the <c>sso_*</c> settings with secrets masked as <c>********</c> (the
/// mask posted back keeps the stored value), and the read-only <c>endpoints</c> to give the identity
/// provider. 404 while SSO is off.
/// </summary>
[Route("support/admin/settings/sso")]
[Authorize(Policy = EscalatedPolicies.Admin)]
[EscalatedPage]
public class SsoSettingsPageController : ControllerBase
{
    public const string Mask = "********";

    private static readonly string[] UrlKeys =
    {
        "sso_url", "sso_login_url", "sso_logout_url", "sso_metadata_url",
        "sso_oauth_authorize_url", "sso_oauth_token_url", "sso_oauth_userinfo_url",
    };

    private readonly SsoService _sso;
    private readonly EscalatedOptions _options;

    public SsoSettingsPageController(SsoService sso, IOptions<EscalatedOptions> options)
    {
        _sso = sso;
        _options = options.Value;
    }

    private string Page => "/" + _options.RoutePrefix.Trim('/') + "/admin/settings/sso";

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var settings = await _sso.ConfigAsync(ct);
        foreach (var key in SsoService.SecretKeys.Where(k => settings[k].Length > 0))
        {
            settings[key] = Mask;
        }

        return UiResults.Render("Escalated/Admin/Settings/SsoSettings", new { settings, endpoints = _sso.Endpoints(Request) });
    }

    [HttpPost]
    public async Task<IActionResult> Update(CancellationToken ct)
    {
        var input = await UiInput.ReadAsync(Request, ct);
        var values = SsoService.Defaults.Keys.Where(input.Has).ToDictionary(k => k, k => (string?)(input.String(k)?.Trim() ?? string.Empty));
        var errors = new Dictionary<string, string>();
        var provider = values.GetValueOrDefault("sso_provider") ?? string.Empty;
        if (!SsoService.Providers.Contains(provider))
        {
            errors["sso_provider"] = "The selected sso provider is invalid.";
        }

        if (values.GetValueOrDefault("sso_jwt_algorithm") is { Length: > 0 } algorithm && !SsoService.JwtAlgorithms.Contains(algorithm))
        {
            errors["sso_jwt_algorithm"] = "The selected sso jwt algorithm is invalid.";
        }

        foreach (var key in UrlKeys)
        {
            if (values.GetValueOrDefault(key) is { Length: > 0 } url
                && !(Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"))
            {
                errors[key] = $"The {key.Replace('_', ' ')} field must be a valid URL.";
            }
        }

        var required = provider switch
        {
            "saml" => new[] { "sso_entity_id", "sso_login_url" },
            "jwt" => new[] { "sso_url", "sso_jwt_secret" },
            "oauth" => new[] { "sso_oauth_authorize_url", "sso_oauth_token_url", "sso_oauth_userinfo_url", "sso_oauth_client_id" },
            _ => Array.Empty<string>(),
        };
        var stored = await _sso.ConfigAsync(ct);
        foreach (var key in required)
        {
            var value = values.GetValueOrDefault(key) ?? string.Empty;
            var keptSecret = value == Mask && stored[key].Length > 0;
            if (value.Length == 0 && !keptSecret)
            {
                errors.TryAdd(key, $"The {key.Replace('_', ' ')} field is required.");
            }
        }

        if (errors.Count > 0)
        {
            return UiResults.Back(HttpContext, Page, errors: errors);
        }

        foreach (var key in SsoService.SecretKeys.Where(k => values.GetValueOrDefault(k) == Mask))
        {
            values.Remove(key);
        }

        await _sso.SaveAsync(values, ct);
        return UiResults.Back(HttpContext, Page, success: "SSO settings updated.");
    }
}
