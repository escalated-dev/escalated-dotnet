using Escalated.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Escalated.Authorization;

/// <summary>
/// Which user has passed the two-factor challenge in this browser session (ADR
/// 2026-10-29-configurable-access-features). Kept in a data-protected, HTTP-only session cookie
/// holding the user id, so a pass for one user does not count for another and the host needs no
/// session middleware (as <see cref="Ui.UiFlash"/>).
///
/// <para>Every new login is challenged again: a cookie-authentication sign-in or sign-out on any
/// scheme clears the mark (Escalated hooks <see cref="CookieAuthenticationEvents"/>), as Laravel
/// clears it on its <c>Login</c> and <c>Logout</c> events. A host that signs users in some other
/// way calls <see cref="Forget"/> on login.</para>
/// </summary>
public static class TwoFactorSession
{
    public const string CookieName = "escalated_two_factor";
    public const string IntendedCookieName = "escalated_two_factor_intended";
    private const string Purpose = "Escalated.TwoFactor.Session";

    public static bool Passed(HttpContext context, string? userId)
    {
        if (userId is null || !context.Request.Cookies.TryGetValue(CookieName, out var cookie) || string.IsNullOrEmpty(cookie))
        {
            return false;
        }

        try
        {
            return Protector(context).Unprotect(cookie) == "passed:" + userId;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static void MarkPassed(HttpContext context, string userId) =>
        context.Response.Cookies.Append(CookieName, Protector(context).Protect("passed:" + userId), Options(context));

    /// <summary>Clears the mark, so the next admin or agent page challenges again.</summary>
    public static void Forget(HttpContext context)
    {
        if (!context.Response.HasStarted)
        {
            context.Response.Cookies.Delete(CookieName, new CookieOptions { Path = "/" });
        }
    }

    public static void RememberIntended(HttpContext context, string url) =>
        context.Response.Cookies.Append(IntendedCookieName, url, Options(context));

    /// <summary>The page asked for before the challenge, once; only a local path.</summary>
    public static string? TakeIntended(HttpContext context)
    {
        if (!context.Request.Cookies.TryGetValue(IntendedCookieName, out var url))
        {
            return null;
        }

        context.Response.Cookies.Delete(IntendedCookieName, new CookieOptions { Path = "/" });
        return url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal) && !url.StartsWith("/\\", StringComparison.Ordinal)
            ? url
            : null;
    }

    private static CookieOptions Options(HttpContext context) => new()
    {
        Path = "/",
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        Secure = context.Request.IsHttps,
    };

    private static IDataProtector Protector(HttpContext context) =>
        context.RequestServices.GetRequiredService<IDataProtectionProvider>().CreateProtector(Purpose);
}

/// <summary>Clears the two-factor mark whenever a cookie-authentication scheme signs someone in or out.</summary>
internal sealed class TwoFactorCookieEvents : IPostConfigureOptions<CookieAuthenticationOptions>
{
    public void PostConfigure(string? name, CookieAuthenticationOptions options)
    {
        var signedIn = options.Events.OnSignedIn;
        options.Events.OnSignedIn = async context =>
        {
            TwoFactorSession.Forget(context.HttpContext);
            await signedIn(context);
        };
        var signingOut = options.Events.OnSigningOut;
        options.Events.OnSigningOut = async context =>
        {
            TwoFactorSession.Forget(context.HttpContext);
            await signingOut(context);
        };
    }
}

/// <summary>
/// The challenge between the host's login and the admin and agent pages, Laravel's
/// <c>RequireTwoFactor</c>. While two-factor is on, after the host's authorization:
/// a user with a confirmed enrolment who has not passed the challenge is sent to
/// <c>{prefix}/two-factor/challenge</c>; while two-factor is required, a staff user without one
/// is sent to enrol and reaches nothing but the enrolment routes. A page visit is redirected
/// (a GET remembered, to return to); a JSON client gets 403 with <c>two_factor_required: true</c>.
/// API token requests are not challenged.
/// </summary>
public sealed class RequireTwoFactorFilter : Microsoft.AspNetCore.Mvc.Filters.IAsyncResourceFilter
{
    private readonly TwoFactorService _twoFactor;
    private readonly IOptions<Configuration.EscalatedOptions> _options;

    public RequireTwoFactorFilter(TwoFactorService twoFactor, IOptions<Configuration.EscalatedOptions> options)
    {
        _twoFactor = twoFactor;
        _options = options;
    }

    public async Task OnResourceExecutionAsync(Microsoft.AspNetCore.Mvc.Filters.ResourceExecutingContext context,
        Microsoft.AspNetCore.Mvc.Filters.ResourceExecutionDelegate next)
    {
        var template = RoutePermissions.Template(context.ActionDescriptor.AttributeRouteInfo?.Template, _options.Value.RoutePrefix);
        var http = context.HttpContext;
        var userId = EscalatedUser.ResolveId(http.User, _options.Value);
        if (!_twoFactor.Enabled || template is null || userId is null || http.Items.ContainsKey("EscalatedApiToken")
            || !(template.StartsWith("/P/admin", StringComparison.Ordinal) || template.StartsWith("/P/agent", StringComparison.Ordinal)))
        {
            await next();
            return;
        }

        var prefix = "/" + _options.Value.RoutePrefix.Trim('/');
        if (await _twoFactor.ConfirmedForAsync(userId, http.RequestAborted) is not null)
        {
            if (TwoFactorSession.Passed(http, userId))
            {
                await next();
                return;
            }

            Deny(context, prefix + "/two-factor/challenge", "Two-factor authentication is required.");
            return;
        }

        var enrolment = template.StartsWith("/P/admin/settings/two-factor", StringComparison.Ordinal);
        if (!enrolment && await _twoFactor.RequiredAsync(http.RequestAborted))
        {
            Deny(context, prefix + "/admin/settings/two-factor", "Set up two-factor authentication to continue.");
            return;
        }

        await next();
    }

    private static void Deny(Microsoft.AspNetCore.Mvc.Filters.ResourceExecutingContext context, string location, string message)
    {
        var request = context.HttpContext.Request;
        var json = !Ui.InertiaHeaders.IsInertia(request)
            && request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);
        if (json)
        {
            context.Result = new Microsoft.AspNetCore.Mvc.ObjectResult(new { message, two_factor_required = true }) { StatusCode = 403 };
            return;
        }

        var get = HttpMethods.IsGet(request.Method);
        if (get)
        {
            TwoFactorSession.RememberIntended(context.HttpContext, request.PathBase + request.Path + request.QueryString);
        }

        context.HttpContext.Response.Headers.Location = request.PathBase + location;
        context.Result = new Microsoft.AspNetCore.Mvc.StatusCodeResult(get ? StatusCodes.Status302Found : StatusCodes.Status303SeeOther);
    }
}
