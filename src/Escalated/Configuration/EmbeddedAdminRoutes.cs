using Escalated.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Escalated.Configuration;

/// <summary>
/// Mounts the admin under the host's prefix while embedding is on (ADR
/// 2026-11-05-embeddable-admin). Every action whose attribute route lives under
/// <c>{RoutePrefix}/admin</c> (the admin screens and the JSON endpoints that share their URLs,
/// plugin admin pages, API tokens, import, reports, settings) answers under
/// <c>Embed:Prefix</c> instead, with the same handler, and its standalone URL is no longer
/// registered. Two-factor enrolment stays where it is. The moved actions get the host's
/// <see cref="EmbedOptions.Policies"/> and <see cref="EmbedOptions.Filters"/>, keep Escalated's
/// admin policy unless <see cref="EmbedOptions.ReplaceAdminGate"/>, and carry
/// <see cref="EscalatedEmbeddedAdminMetadata"/>. While embedding is off this changes nothing.
/// </summary>
internal sealed class EmbeddedAdminActionDescriptorProvider : IActionDescriptorProvider
{
    private readonly IOptions<EscalatedOptions> _options;

    public EmbeddedAdminActionDescriptorProvider(IOptions<EscalatedOptions> options)
    {
        _options = options;
    }

    // After MVC's own providers (ControllerActionDescriptorProvider is -1000).
    public int Order => 1000;

    public void OnProvidersExecuting(ActionDescriptorProviderContext context)
    {
    }

    public void OnProvidersExecuted(ActionDescriptorProviderContext context)
    {
        var options = _options.Value;
        if (!EscalatedEmbed.Enabled(options))
        {
            return;
        }

        var embed = options.Ui.Embed;
        var prefix = EscalatedEmbed.Prefix(options);

        foreach (var action in context.Results.OfType<ControllerActionDescriptor>())
        {
            var route = action.AttributeRouteInfo;
            if (route?.Template is not { } template
                || action.ControllerTypeInfo.Assembly != typeof(EmbeddedAdminActionDescriptorProvider).Assembly)
            {
                continue;
            }

            var rest = EscalatedEmbed.StandaloneAdminRest(template, options.RoutePrefix);
            if (rest is null)
            {
                continue;
            }

            route.Template = rest.Length == 0 ? prefix : prefix + "/" + rest;

            var metadata = action.EndpointMetadata.ToList();
            if (embed.ReplaceAdminGate)
            {
                metadata.RemoveAll(m => m is IAuthorizeData data && data.Policy == EscalatedPolicies.Admin);
                metadata.Add(new AuthorizeAttribute(EscalatedPolicies.User));
            }

            foreach (var policy in embed.Policies ?? new List<string>())
            {
                if (!string.IsNullOrWhiteSpace(policy))
                {
                    metadata.Add(new AuthorizeAttribute(policy.Trim()));
                }
            }

            metadata.Add(new EscalatedEmbeddedAdminMetadata(template));
            action.EndpointMetadata = metadata;

            if (embed.Filters.Count > 0)
            {
                var filters = action.FilterDescriptors.ToList();
                filters.AddRange(embed.Filters.Select(f => new FilterDescriptor(f, FilterScope.Controller)));
                action.FilterDescriptors = filters
                    .Select((f, i) => (f, i))
                    .OrderBy(x => x.f.Order).ThenBy(x => x.f.Scope).ThenBy(x => x.i)
                    .Select(x => x.f)
                    .ToList();
            }
        }
    }
}

/// <summary>
/// iframe mode: <c>Content-Security-Policy: frame-ancestors 'self' &lt;origins&gt;</c> added to, and
/// <c>X-Frame-Options</c> removed from, embedded admin responses only. The middleware is the
/// outermost one (a startup filter), and its callback is the first registered, so it runs last,
/// after every other middleware has set its headers: nothing the host adds can undo it. Over
/// HTTPS, Escalated's own cookies on those responses go out as SameSite=None; Secure. A host
/// CSP is kept; the directive is added as a second policy. Not installed while embedding is off
/// or in layout mode.
/// </summary>
internal sealed class EmbedFramingStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        var options = app.ApplicationServices.GetRequiredService<IOptions<EscalatedOptions>>().Value;
        if (EscalatedEmbed.Enabled(options) && EscalatedEmbed.Mode(options) == EmbedOptions.IframeMode)
        {
            var directive = EscalatedEmbed.FrameAncestorsDirective(options);
            app.Use((context, nextMiddleware) =>
            {
                context.Response.OnStarting(() =>
                {
                    if (EscalatedEmbed.IsEmbeddedRequest(context))
                    {
                        context.Response.Headers.Remove("X-Frame-Options");
                        context.Response.Headers.Append("Content-Security-Policy", directive);
                        if (context.Request.IsHttps)
                        {
                            ThirdPartyCookies(context.Response);
                        }
                    }

                    return Task.CompletedTask;
                });
                return nextMiddleware(context);
            });
        }

        next(app);
    };

    /// <summary>
    /// Escalated's own cookies set by a framed response (the flash message, the import
    /// wizard's credentials) are <c>SameSite=Lax</c>, which a cross-site frame never sends back.
    /// On embedded HTTPS responses in iframe mode they go out as <c>SameSite=None; Secure</c>.
    /// The host's own cookies (its sign-in) are its to configure.
    /// </summary>
    internal static void ThirdPartyCookies(HttpResponse response)
    {
        var cookies = response.Headers.SetCookie;
        if (cookies.Count == 0)
        {
            return;
        }

        var rewritten = cookies.Select(cookie =>
        {
            if (cookie is null || !cookie.StartsWith("escalated_", StringComparison.Ordinal))
            {
                return cookie;
            }

            var parts = cookie.Split(';').Select(p => p.Trim())
                .Where(p => !p.StartsWith("samesite=", StringComparison.OrdinalIgnoreCase)
                    && !p.Equals("secure", StringComparison.OrdinalIgnoreCase))
                .ToList();
            parts.Add("secure");
            parts.Add("samesite=none");
            return string.Join("; ", parts);
        }).ToArray();
        response.Headers.SetCookie = rewritten;
    }
}
