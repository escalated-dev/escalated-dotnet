using System.Net.Http.Headers;
using System.Text.Json;
using Escalated.Configuration;
using Escalated.Tests.Hosting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Escalated.Tests.Embed;

/// <summary>
/// The host side of an embedded admin (ADR 2026-11-05-embeddable-admin): a host policy that lets
/// only users carrying <c>host_admin=yes</c> in, a host filter that marks every response it ran
/// on, and host middleware that sends <c>X-Frame-Options</c> (and in iframe tests a CSP) on every
/// response, set as late as it can be.
/// </summary>
internal static class EmbedTestSupport
{
    public const string Prefix = "backoffice/helpdesk";
    public const string HostPolicy = "host-admin";
    public const string Admin = "admin-1";
    public const string Agent = "agent-1";
    public const string Plain = "user-1";

    public static readonly (string, string) HostClaim = ("host_admin", "yes");

    public static async Task<EscalatedTestHost> StartAsync(
        Action<EscalatedOptions>? embed = null,
        Action<EscalatedOptions>? options = null,
        Action<IServiceCollection>? services = null,
        bool hostCsp = false)
    {
        var host = await EscalatedTestHost.StartAsync(
            o =>
            {
                o.Ui.Enabled = true;
                if (embed is null)
                {
                    o.EmbedAdmin(prefix: Prefix, policies: new[] { HostPolicy }, layout: "Layouts/AdminLayout",
                        filters: new IFilterMetadata[] { new HostHeaderFilter() });
                }
                else
                {
                    embed(o);
                }

                options?.Invoke(o);
            },
            s =>
            {
                s.AddAuthorization(a => a.AddPolicy(HostPolicy, p => p.RequireClaim("host_admin", "yes")));
                services?.Invoke(s);
            },
            app => app.Use((context, next) =>
            {
                context.Response.OnStarting(() =>
                {
                    context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
                    if (hostCsp)
                    {
                        context.Response.Headers.Append("Content-Security-Policy", "default-src 'self'");
                    }

                    return Task.CompletedTask;
                });
                return next(context);
            }));

        await host.GrantRoleAsync(Admin, "escalated-admin");
        await host.GrantRoleAsync(Agent, "escalated-agent");
        return host;
    }

    /// <summary>A first visit (a browser navigation), not an Inertia visit.</summary>
    public static HttpRequestMessage Browse(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        return request;
    }

    public static JsonElement SharedProps(JsonElement page) => page.GetProperty("props").GetProperty("escalated");

    /// <summary>Marks a response the host filter ran on.</summary>
    public sealed class HostHeaderFilter : IActionFilter
    {
        public void OnActionExecuting(ActionExecutingContext context) =>
            context.HttpContext.Response.Headers["X-Host-Admin"] = "1";

        public void OnActionExecuted(ActionExecutedContext context)
        {
        }
    }
}
