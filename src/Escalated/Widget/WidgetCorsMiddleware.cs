using System.Globalization;
using System.Security.Claims;
using Escalated.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Escalated.Widget;

/// <summary>
/// CORS for the widget routes (<c>/support/widget/*</c>, including the chat
/// routes). Only origins <see cref="WidgetOriginPolicy"/> allows get CORS
/// headers; such requests always run as a guest. Every other request passes
/// untouched, so nothing changes until an origin is listed.
///
/// <para><c>AddEscalated()</c> puts this at the front of the pipeline with an
/// <see cref="IStartupFilter"/>, so preflights are answered before routing and
/// every answer, a 4xx or 429 from a filter included, carries the grant.</para>
/// </summary>
public sealed class WidgetCorsMiddleware
{
    public const string AllowMethods = "GET, POST, OPTIONS";
    public const string AllowHeaders = "Accept, Authorization, Content-Type, X-Requested-With";

    private readonly RequestDelegate _next;

    public WidgetCorsMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        if (!request.Path.StartsWithSegments(WidgetOrigins.WidgetPath)
            || request.Path.Equals(WidgetOrigins.ScriptPath, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var origin = WidgetOrigins.CrossOrigin(request);
        var allowed = origin is not null
            && await context.RequestServices.GetRequiredService<WidgetOriginPolicy>().AllowsAsync(origin, context.RequestAborted);

        if (HttpMethods.IsOptions(request.Method) && request.Headers.ContainsKey("Access-Control-Request-Method"))
        {
            var response = context.Response;
            response.StatusCode = StatusCodes.Status204NoContent;
            AppendVary(response.Headers);
            if (allowed)
            {
                var maxAge = context.RequestServices.GetRequiredService<IOptions<EscalatedOptions>>().Value.Widget.PreflightMaxAge;
                response.Headers["Access-Control-Allow-Methods"] = AllowMethods;
                response.Headers["Access-Control-Allow-Headers"] = AllowHeaders;
                response.Headers["Access-Control-Max-Age"] = maxAge.ToString(CultureInfo.InvariantCulture);
                AddGrant(response.Headers, origin!);
            }

            return;
        }

        if (!allowed)
        {
            await _next(context);
            return;
        }

        // A cross-site embed is always anonymous: never act as whoever is
        // signed in to the host in the same browser. WidgetGuestFilter repeats
        // this after the host's authentication middleware has run.
        context.Items[WidgetOrigins.ItemKey] = origin;
        context.User = Anonymous();

        context.Response.OnStarting(() =>
        {
            AddGrant(context.Response.Headers, origin!);
            return Task.CompletedTask;
        });

        await _next(context);
    }

    internal static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());

    private static void AddGrant(IHeaderDictionary headers, string origin)
    {
        headers["Access-Control-Allow-Origin"] = origin;
        headers["Access-Control-Expose-Headers"] = "Retry-After";
        AppendVary(headers);
    }

    private static void AppendVary(IHeaderDictionary headers)
    {
        var vary = headers.Vary
            .SelectMany(value => (value ?? string.Empty).Split(','))
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .ToList();

        if (!vary.Contains("Origin", StringComparer.OrdinalIgnoreCase))
        {
            vary.Add("Origin");
        }

        headers.Vary = new StringValues(string.Join(", ", vary));
    }
}

/// <summary>
/// Puts <see cref="WidgetCorsMiddleware"/> at the front of the host's pipeline.
/// Registered by <c>AddEscalated()</c>.
/// </summary>
internal sealed class WidgetCorsStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.UseMiddleware<WidgetCorsMiddleware>();
        next(app);
    };
}

/// <summary>
/// Runs after the host's authentication middleware, which would otherwise sign
/// the browser's session user back in, and drops that user for a widget request
/// from an allowed other site. The origin is checked again here: a host's
/// <c>UseForwardedHeaders()</c> may have corrected the scheme and host since
/// <see cref="WidgetCorsMiddleware"/> ran.
/// </summary>
internal sealed class WidgetGuestFilter : IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var http = context.HttpContext;
        if (!http.Items.ContainsKey(WidgetOrigins.ItemKey))
        {
            return;
        }

        if (WidgetOrigins.CrossOrigin(http.Request) is null)
        {
            http.Items.Remove(WidgetOrigins.ItemKey);
            return;
        }

        http.User = WidgetCorsMiddleware.Anonymous();
    }
}
