using System.Net;
using System.Text;
using Escalated.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Escalated.Ui;

/// <summary>
/// The HTML document a first, non-Inertia visit gets: Laravel's root Blade view
/// (<c>app.blade.php</c> with <c>@inertia</c>). Register your own implementation
/// to render the page inside your layout (a Razor view, your Vite tags); the
/// default one is configured through <see cref="UiOptions"/>.
/// </summary>
public interface IEscalatedUiRootView
{
    /// <param name="pageJson">The page object, already serialized.</param>
    Task<string> RenderAsync(HttpContext context, InertiaPage page, string pageJson, CancellationToken ct = default);
}

/// <summary>
/// A minimal document: <see cref="UiOptions.HeadHtml"/>, the route helper, the
/// page object (as Inertia 3's <c>&lt;script data-page&gt;</c> and as Inertia 1/2's
/// <c>data-page</c> attribute, so either client version boots), and
/// <see cref="UiOptions.BodyHtml"/>.
/// </summary>
public sealed class DefaultEscalatedUiRootView : IEscalatedUiRootView
{
    private readonly IOptions<EscalatedOptions> _options;
    private readonly IOptions<UiRouteOptions> _routes;

    public DefaultEscalatedUiRootView(IOptions<EscalatedOptions> options, IOptions<UiRouteOptions> routes)
    {
        _options = options;
        _routes = routes;
    }

    public Task<string> RenderAsync(HttpContext context, InertiaPage page, string pageJson, CancellationToken ct = default)
    {
        var options = _options.Value;
        var ui = options.Ui;
        var html = new StringBuilder();

        html.Append("<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n");
        html.Append("<meta charset=\"utf-8\">\n");
        html.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        html.Append("<title>").Append(WebUtility.HtmlEncode(ui.Title)).Append("</title>\n");
        html.Append(Head(options, _routes.Value));
        html.Append("</head>\n<body>\n");
        html.Append(Mount(options, pageJson));
        if (!string.IsNullOrEmpty(ui.BodyHtml))
        {
            html.Append(ui.BodyHtml).Append('\n');
        }
        html.Append("</body>\n</html>\n");

        return Task.FromResult(html.ToString());
    }

    /// <summary>
    /// What the default view writes in <c>&lt;head&gt;</c> after the title: the route helper
    /// (following the admin while it is embedded) and <see cref="UiOptions.HeadHtml"/>.
    /// </summary>
    internal static string Head(EscalatedOptions options, UiRouteOptions routes)
    {
        var ui = options.Ui;
        var html = new StringBuilder();
        if (ui.RouteHelper)
        {
            html.Append("<script>").Append(RouteHelperScript(UiRoutes.Resolve(routes.Routes, options))).Append("</script>\n");
        }
        if (!string.IsNullOrEmpty(ui.HeadHtml))
        {
            html.Append(ui.HeadHtml).Append('\n');
        }

        return html.ToString();
    }

    /// <summary>The page object as Inertia 3's <c>&lt;script data-page&gt;</c> and as Inertia 1/2's mount element.</summary>
    internal static string Mount(EscalatedOptions options, string pageJson)
    {
        var id = WebUtility.HtmlEncode(options.Ui.RootElementId);
        var html = new StringBuilder();
        html.Append("<script data-page=\"").Append(id).Append("\" type=\"application/json\">")
            .Append(ScriptSafe(pageJson)).Append("</script>\n");
        html.Append("<div id=\"").Append(id).Append("\" data-page=\"").Append(WebUtility.HtmlEncode(pageJson)).Append("\"></div>\n");
        return html.ToString();
    }

    /// <summary>
    /// JSON inside a script element ends at the first <c>&lt;/</c>, so a ticket subject
    /// reading <c>&lt;/script&gt;</c> must not be written as is.
    /// </summary>
    internal static string ScriptSafe(string json) =>
        json.Replace("<", "\\u003c").Replace(">", "\\u003e").Replace("&", "\\u0026")
            .Replace("\u2028", "\\u2028").Replace("\u2029", "\\u2029");

    /// <summary>
    /// <c>window.route(name, params)</c>, the subset of Ziggy the pages use: params
    /// fill the <c>{placeholders}</c> in order (a scalar, an array, or an object by
    /// name), and what is left of an object goes into the query string.
    /// </summary>
    internal static string RouteHelperScript(IReadOnlyDictionary<string, string> routes)
    {
        var map = ScriptSafe(System.Text.Json.JsonSerializer.Serialize(routes));
        return "(function(){var routes=" + map + ";"
            + "if(typeof window.route==='function'){return;}"
            + "window.EscalatedRoutes=routes;"
            + "window.route=function(name,params){"
            + "var template=routes[name];"
            + "if(template===undefined){console.warn('[escalated] no route named '+name);return '#';}"
            + "var list=params===undefined||params===null?[]:(Array.isArray(params)?params.slice():(typeof params==='object'?null:[params]));"
            + "var rest=list===null?Object.assign({},params):{};"
            + "var url=template.replace(/\\{(\\w+)\\??\\}/g,function(_,key){var value;"
            + "if(list!==null){value=list.shift();}else{value=rest[key];delete rest[key];}"
            + "if(value!==null&&typeof value==='object'){value=value.reference!==undefined?value.reference:value.id;}"
            + "return value===undefined||value===null?'':encodeURIComponent(value);});"
            + "var query=Object.keys(rest).filter(function(k){return rest[k]!==undefined&&rest[k]!==null&&rest[k]!=='';})"
            + ".map(function(k){return encodeURIComponent(k)+'='+encodeURIComponent(rest[k]);}).join('&');"
            + "return query?url+'?'+query:url;};})();";
    }
}
