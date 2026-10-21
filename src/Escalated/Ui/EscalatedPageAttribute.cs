using Escalated.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Escalated.Ui;

/// <summary>
/// Marks a page action: it matches only when <see cref="UiOptions.Enabled"/> is on,
/// so with the UI off the route does not exist (404) and API-only hosts are unchanged.
///
/// <para>Several page routes share a path with a JSON API action
/// (<c>GET support/agent/tickets</c>, <c>GET support/admin/settings</c>). With
/// <see cref="Negotiate"/> the page action also needs the request to be a page
/// request: an Inertia visit (<c>X-Inertia: true</c>), or a browser navigation
/// (a GET whose <c>Accept</c> names <c>text/html</c>). ASP.NET prefers an action
/// whose constraints pass over one with none, so a page request reaches the page
/// and every other request still reaches the API action.</para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class EscalatedPageAttribute : Attribute, IActionConstraint
{
    /// <summary>Also require a page request; for routes the JSON API answers too.</summary>
    public bool Negotiate { get; set; }

    public int Order => 0;

    public bool Accept(ActionConstraintContext context)
    {
        var http = context.RouteContext.HttpContext;
        var options = http.RequestServices.GetRequiredService<IOptions<EscalatedOptions>>().Value;
        if (!options.Ui.Enabled)
        {
            return false;
        }

        return !Negotiate || IsPageRequest(http.Request);
    }

    public static bool IsPageRequest(HttpRequest request)
    {
        if (InertiaHeaders.IsInertia(request))
        {
            return true;
        }

        return HttpMethods.IsGet(request.Method)
            && request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase);
    }
}
