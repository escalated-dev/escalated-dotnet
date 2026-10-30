using Escalated.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Escalated.Ui;

/// <summary>
/// Answers an Inertia visit to one of Escalated's own routes that ends in 403, 404,
/// 419, 429, 500 or 503 with the shared frontend's <c>Escalated/Error</c> page.
///
/// <para>Without it the Inertia client gets the host's error body back (a JSON
/// problem, an empty 403 from the authorization middleware, an HTML 500) and can
/// only show it in a modal over the screen the user was on. The props are the ones
/// Rails and Laravel send: <c>status</c> and a generic <c>message</c>. Whatever the
/// controller, a filter or an exception said never reaches the page.</para>
///
/// <para>It sits at the front of the pipeline, so it sees refusals from the host's
/// authorization middleware as well as Escalated's filters and actions, and an
/// exception nothing else handled. It answers only when all of these hold:
/// <see cref="UiOptions.Enabled"/> is on; the request is an Inertia visit
/// (<c>X-Inertia: true</c>); the matched endpoint is one of Escalated's controllers
/// that serve the UI (not the JSON API, widget, inbound email, SSO, plugin HTTP or
/// newsletter tracking ones); the response is not already an Inertia page. Full page
/// loads, JSON clients, unmatched URLs, the host's own routes and redirects are left
/// alone. In the Development environment a 500 is left to the developer exception
/// page.</para>
/// </summary>
public sealed class InertiaErrorPageMiddleware
{
    public const string Page = "Escalated/Error";

    /// <summary>The statuses the page has wording for, and the message each one gets.</summary>
    public static readonly IReadOnlyDictionary<int, string> Messages = new Dictionary<int, string>
    {
        [403] = "You are not authorized to perform this action.",
        [404] = "The requested resource was not found.",
        [419] = "Your session has expired. Refresh the page and try again.",
        [429] = "Too many requests. Wait a moment and try again.",
        [500] = "Something went wrong on our end.",
        [503] = "The service is temporarily unavailable. Try again shortly.",
    };

    /// <summary>Escalated controllers that do not serve the UI.</summary>
    private static readonly string[] ExcludedNamespaces =
    {
        "Escalated.Controllers.Api",
        "Escalated.Controllers.Widget",
    };

    private static readonly HashSet<string> ExcludedControllers = new(StringComparer.Ordinal)
    {
        "Escalated.Controllers.InboundEmailController",
        "Escalated.Controllers.SsoController",
        "Escalated.Controllers.PluginHttpController",
        "Escalated.Controllers.Newsletter.NewsletterEspWebhookController",
        "Escalated.Controllers.Newsletter.NewsletterPublicController",
    };

    private readonly RequestDelegate _next;
    private readonly IOptions<EscalatedOptions> _options;
    private readonly IHostEnvironment? _environment;
    private readonly ILogger<InertiaErrorPageMiddleware> _logger;

    public InertiaErrorPageMiddleware(
        RequestDelegate next,
        IOptions<EscalatedOptions> options,
        ILogger<InertiaErrorPageMiddleware> logger,
        IHostEnvironment? environment = null)
    {
        _next = next;
        _options = options;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_options.Value.Ui.Enabled || !InertiaHeaders.IsInertia(context.Request))
        {
            await _next(context);
            return;
        }

        // An Inertia visit's answer is a small JSON page object or a redirect, so it
        // is held back until it is known whether it is an error to replace.
        var original = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            try
            {
                await _next(context);
            }
            catch (Exception exception) when (Answers(context, 500) && !context.Response.HasStarted)
            {
                _logger.LogError(exception, "Unhandled exception on {Path}; answered with the Escalated error page.", context.Request.Path);
                context.Response.StatusCode = 500;
                context.Response.Headers.Remove(InertiaHeaders.Inertia);
            }

            var status = context.Response.StatusCode;
            if (!context.Response.HasStarted && Answers(context, status)
                && string.IsNullOrEmpty(context.Response.Headers[InertiaHeaders.Inertia]))
            {
                context.Response.Body = original;
                await RenderAsync(context, status);
                return;
            }

            context.Response.Body = original;
            buffer.Position = 0;
            await buffer.CopyToAsync(original, context.RequestAborted);
        }
        finally
        {
            context.Response.Body = original;
        }
    }

    private bool Answers(HttpContext context, int status) =>
        Messages.ContainsKey(status)
        && !(status == 500 && _environment?.IsDevelopment() == true)
        && IsEscalatedUiRoute(context.GetEndpoint());

    /// <summary>Whether the endpoint is one of Escalated's controllers that serve the UI.</summary>
    public static bool IsEscalatedUiRoute(Endpoint? endpoint)
    {
        var action = endpoint?.Metadata.GetMetadata<ControllerActionDescriptor>();
        var type = action?.ControllerTypeInfo;
        if (type is null || type.Assembly != typeof(InertiaErrorPageMiddleware).Assembly)
        {
            return false;
        }

        var name = type.FullName ?? string.Empty;
        return !ExcludedControllers.Contains(name)
            && !ExcludedNamespaces.Any(ns => name.StartsWith(ns + ".", StringComparison.Ordinal));
    }

    private static async Task RenderAsync(HttpContext context, int status)
    {
        var response = context.Response;
        response.Clear();

        var result = new InertiaResult(Page, new Dictionary<string, object?>
        {
            ["status"] = status,
            ["message"] = Messages[status],
        })
        { StatusCode = status };

        var actionContext = new ActionContext(context, context.GetRouteData() ?? new RouteData(), new ActionDescriptor());
        try
        {
            await result.ExecuteResultAsync(actionContext);
        }
        catch (Exception) when (!response.HasStarted)
        {
            // The page itself could not be built (its shared props read the
            // database, which may be what failed): the bare status, no body.
            response.Clear();
            response.StatusCode = status;
        }
    }
}

/// <summary>Puts <see cref="InertiaErrorPageMiddleware"/> at the front of the host's pipeline.</summary>
internal sealed class InertiaErrorPageStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.UseMiddleware<InertiaErrorPageMiddleware>();
        next(app);
    };
}
