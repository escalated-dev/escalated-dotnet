using System.Text;
using Escalated.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Escalated.Ui;

/// <summary>The Inertia page object.</summary>
public sealed class InertiaPage
{
    public string Component { get; init; } = string.Empty;
    public IDictionary<string, object?> Props { get; init; } = new Dictionary<string, object?>();
    public string Url { get; init; } = "/";
    public string? Version { get; init; }
    public IDictionary<string, string> Flash { get; init; } = new Dictionary<string, string>();

    /// <summary>The wire form: keys as the Inertia client reads them.</summary>
    public Dictionary<string, object?> ToWire() => new()
    {
        ["component"] = Component,
        ["props"] = Props,
        ["url"] = Url,
        ["version"] = Version,
        ["encryptHistory"] = false,
        ["clearHistory"] = false,
        ["flash"] = Flash,
    };
}

/// <summary>Request headers of the Inertia protocol.</summary>
public static class InertiaHeaders
{
    public const string Inertia = "X-Inertia";
    public const string Version = "X-Inertia-Version";
    public const string Location = "X-Inertia-Location";
    public const string PartialComponent = "X-Inertia-Partial-Component";
    public const string PartialData = "X-Inertia-Partial-Data";
    public const string PartialExcept = "X-Inertia-Partial-Except";

    public static bool IsInertia(HttpRequest request) =>
        string.Equals(request.Headers[Inertia].ToString(), "true", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Renders an Escalated page. With <see cref="UiOptions.Enabled"/> it speaks the
/// Inertia protocol: an Inertia visit gets the page object as JSON
/// (<c>X-Inertia: true</c>, <c>Vary: X-Inertia</c>), a stale asset version on a
/// GET gets <c>409</c> with <c>X-Inertia-Location</c>, partial reloads get only the
/// props they asked for, and any other request gets the root view's HTML.
///
/// <para>With the UI off it answers <c>{component, props}</c> as JSON, which is how
/// the newsletter screens have always answered, so an API-only host is unchanged
/// apart from the props now reading as the pages do (snake_case).</para>
/// </summary>
public sealed class InertiaResult : IActionResult
{
    public InertiaResult(string component, object? props)
    {
        Component = component;
        Props = UiJson.TopLevel(props);
    }

    public string Component { get; }

    /// <summary>The page's own props, before shared props are merged in.</summary>
    public Dictionary<string, object?> Props { get; }

    public int StatusCode { get; init; } = StatusCodes.Status200OK;

    public async Task ExecuteResultAsync(ActionContext context)
    {
        var http = context.HttpContext;
        var request = http.Request;
        var response = http.Response;
        var services = http.RequestServices;
        var options = services.GetRequiredService<IOptions<EscalatedOptions>>().Value;
        var ct = http.RequestAborted;

        if (!options.Ui.Enabled)
        {
            response.StatusCode = StatusCode;
            response.ContentType = "application/json; charset=utf-8";
            await response.WriteAsync(UiJson.Serialize(new Dictionary<string, object?>
            {
                ["component"] = Component,
                ["props"] = Props,
            }), ct);
            return;
        }

        var url = request.PathBase + request.Path + request.QueryString;
        var isInertia = InertiaHeaders.IsInertia(request);

        if (isInertia && HttpMethods.IsGet(request.Method) && options.Ui.Version is { } version
            && request.Headers[InertiaHeaders.Version].ToString() != version)
        {
            response.StatusCode = StatusCodes.Status409Conflict;
            response.Headers[InertiaHeaders.Location] = url.ToString();
            return;
        }

        var flash = UiFlash.Take(http);
        var props = new Dictionary<string, object?>();
        foreach (var shared in services.GetServices<IEscalatedUiSharedProps>())
        {
            await shared.ShareAsync(http, props, ct);
        }

        props["flash"] = new Dictionary<string, object?>
        {
            ["success"] = flash.Flash.GetValueOrDefault("success"),
            ["error"] = flash.Flash.GetValueOrDefault("error"),
        };
        props["errors"] = flash.Errors;

        foreach (var (key, value) in Props)
        {
            props[key] = value;
        }

        if (isInertia && request.Headers[InertiaHeaders.PartialComponent].ToString() == Component)
        {
            props = Partial(props, request);
        }

        var page = new InertiaPage
        {
            Component = Component,
            Props = props,
            Url = url.ToString(),
            Version = options.Ui.Version,
            Flash = flash.Flash,
        };
        var json = UiJson.Serialize(page.ToWire());

        response.Headers.Vary = InertiaHeaders.Inertia;
        response.StatusCode = StatusCode;

        if (isInertia)
        {
            response.Headers[InertiaHeaders.Inertia] = "true";
            response.ContentType = "application/json; charset=utf-8";
            await response.WriteAsync(json, Encoding.UTF8, ct);
            return;
        }

        var view = services.GetRequiredService<IEscalatedUiRootView>();
        var html = await view.RenderAsync(http, page, json, ct);
        response.ContentType = "text/html; charset=utf-8";
        await response.WriteAsync(html, Encoding.UTF8, ct);
    }

    /// <summary>
    /// <c>X-Inertia-Partial-Data</c> keeps only the listed props, and
    /// <c>X-Inertia-Partial-Except</c> drops the listed ones. <c>errors</c> always stays.
    /// </summary>
    private static Dictionary<string, object?> Partial(Dictionary<string, object?> props, HttpRequest request)
    {
        static HashSet<string> List(string header) =>
            header.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();

        var only = List(request.Headers[InertiaHeaders.PartialData].ToString());
        var except = List(request.Headers[InertiaHeaders.PartialExcept].ToString());

        return props
            .Where(pair => pair.Key == "errors" || ((only.Count == 0 || only.Contains(pair.Key)) && !except.Contains(pair.Key)))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
    }
}

/// <summary>
/// A redirect after a page action, carrying a flash message or validation errors.
/// Inertia visits follow a <c>303</c> with a GET; a <c>302</c> after PUT, PATCH or
/// DELETE would be replayed with the same method, so every non-GET redirect here
/// is a 303 (as inertia-laravel's middleware makes it).
/// </summary>
public sealed class UiRedirectResult : IActionResult
{
    public UiRedirectResult(string url)
    {
        Url = url;
    }

    public string Url { get; }
    public string? Success { get; init; }
    public string? Error { get; init; }
    public IReadOnlyDictionary<string, string>? Errors { get; init; }

    public Task ExecuteResultAsync(ActionContext context)
    {
        var http = context.HttpContext;
        if (Success is not null) UiFlash.Put(http, "success", Success);
        if (Error is not null) UiFlash.Put(http, "error", Error);
        if (Errors is { Count: > 0 }) UiFlash.PutErrors(http, Errors);

        http.Response.StatusCode = HttpMethods.IsGet(http.Request.Method)
            ? StatusCodes.Status302Found
            : StatusCodes.Status303SeeOther;
        http.Response.Headers.Location = Url;
        return Task.CompletedTask;
    }
}

/// <summary>Helpers the page controllers use.</summary>
public static class UiResults
{
    public static InertiaResult Render(string component, object? props = null) => new(component, props);

    /// <summary>
    /// Back to the page the request came from (<c>Referer</c>), as Laravel's
    /// <c>back()</c>, or to <paramref name="fallback"/> when the request names none
    /// or one on another host.
    /// </summary>
    public static UiRedirectResult Back(HttpContext context, string fallback, string? success = null, string? error = null,
        IReadOnlyDictionary<string, string>? errors = null)
    {
        var target = fallback;
        var referer = context.Request.Headers.Referer.ToString();
        if (Uri.TryCreate(referer, UriKind.Absolute, out var uri)
            && string.Equals(uri.Authority, context.Request.Host.Value, StringComparison.OrdinalIgnoreCase))
        {
            target = uri.PathAndQuery;
        }

        return new UiRedirectResult(target) { Success = success, Error = error, Errors = errors };
    }
}
