using System.Net;
using System.Text.RegularExpressions;
using Escalated.Data;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace Escalated.Tests.Hosting;

/// <summary>
/// Boots MVC over the package's controllers the way a host does.
///
/// <para>MVC requires every action on an <c>[ApiController]</c> to be attribute
/// routed. It checks when it first builds the endpoint table, which is on the
/// first request, and throws for the whole application if any action is not.
/// The newsletter controllers had no routes, so a host that followed the README
/// served nothing at all, not only no newsletters.</para>
/// </summary>
public class MvcBootTests
{
    [Fact]
    public async Task HostServesARequest()
    {
        await using var host = await EscalatedTestHost.StartAsync();

        var response = await host.Client.GetAsync("/support/admin/tickets");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task EveryActionInThePackageIsRouted()
    {
        await using var host = await EscalatedTestHost.StartAsync();

        var routed = host.Endpoints
            .Select(e => e.Metadata.GetMetadata<ControllerActionDescriptor>())
            .Where(d => d is not null)
            .Select(d => d!.MethodInfo)
            .ToHashSet();

        var actions = typeof(EscalatedDbContext).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true } && t.Name.EndsWith("Controller"))
            .SelectMany(t => t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
            .Where(m => !m.IsSpecialName && m.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.NonActionAttribute), true).Length == 0)
            .ToList();

        var unrouted = actions.Where(m => !routed.Contains(m)).Select(m => $"{m.DeclaringType!.Name}.{m.Name}").ToList();

        Assert.True(unrouted.Count == 0, "Actions with no route:\n" + string.Join("\n", unrouted));
    }

    /// <summary>
    /// The paths the NestJS reference serves newsletters on and the shared
    /// frontend's newsletter pages call. Not under the <c>support/</c> prefix.
    /// </summary>
    public static IEnumerable<object[]> NewsletterRoutes() => new[]
    {
        new object[] { "GET", "admin/newsletters" },
        new object[] { "GET", "admin/newsletters/new" },
        new object[] { "POST", "admin/newsletters" },
        new object[] { "POST", "admin/newsletters/preview" },
        new object[] { "POST", "admin/newsletters/test" },
        new object[] { "GET", "admin/newsletters/{newsletter}" },
        new object[] { "GET", "admin/newsletters/{newsletter}/edit" },
        new object[] { "PUT", "admin/newsletters/{newsletter}" },
        new object[] { "DELETE", "admin/newsletters/{newsletter}" },

        new object[] { "GET", "admin/newsletters/lists" },
        new object[] { "GET", "admin/newsletters/lists/new" },
        new object[] { "POST", "admin/newsletters/lists" },
        new object[] { "GET", "admin/newsletters/lists/{list}" },
        new object[] { "PUT", "admin/newsletters/lists/{list}" },
        new object[] { "DELETE", "admin/newsletters/lists/{list}" },
        new object[] { "POST", "admin/newsletters/lists/{list}/members" },
        new object[] { "DELETE", "admin/newsletters/lists/{list}/members/{contactId}" },
        new object[] { "POST", "admin/newsletters/lists/{list}/import" },

        new object[] { "GET", "admin/newsletters/templates" },
        new object[] { "GET", "admin/newsletters/templates/new" },
        new object[] { "POST", "admin/newsletters/templates" },
        new object[] { "GET", "admin/newsletters/templates/{template}" },
        new object[] { "PUT", "admin/newsletters/templates/{template}" },
        new object[] { "DELETE", "admin/newsletters/templates/{template}" },

        new object[] { "GET", "admin/newsletters/settings" },
        new object[] { "PUT", "admin/newsletters/settings" },

        new object[] { "GET", "escalated/n/o/{token}" },
        new object[] { "GET", "escalated/n/c/{token}" },
        new object[] { "GET", "escalated/n/u/{token}" },
        new object[] { "POST", "escalated/n/u/{token}" },
        new object[] { "GET", "escalated/n/v/{token}" },

        new object[] { "POST", "escalated/webhooks/newsletter/postmark" },
        new object[] { "POST", "escalated/webhooks/newsletter/mailgun" },
        new object[] { "POST", "escalated/webhooks/newsletter/ses" },
        new object[] { "POST", "escalated/webhooks/newsletter/sendgrid" },
    };

    [Theory]
    [MemberData(nameof(NewsletterRoutes))]
    public async Task NewsletterRouteMatchesTheReference(string method, string path)
    {
        await using var host = await EscalatedTestHost.StartAsync();

        var served = host.Endpoints
            .Where(e => e.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.Namespace == "Escalated.Controllers.Newsletter")
            .Where(e => e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method) == true)
            .Select(e => WithoutConstraints(e.RoutePattern.RawText!))
            .ToList();

        Assert.Contains(path, served);
    }

    [Fact]
    public async Task NewslettersDisabled_ReturnNotFound()
    {
        await using var host = await EscalatedTestHost.StartAsync();

        var response = await host.Client.GetAsync("/escalated/n/o/abc123.gif");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task NewslettersEnabled_ServeTheTrackingPixel()
    {
        await using var host = await EscalatedTestHost.StartAsync(o => o.EnableNewsletters = true);

        var response = await host.Client.GetAsync("/escalated/n/o/abc123.gif");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/gif", response.Content.Headers.ContentType?.MediaType);
    }

    private static string WithoutConstraints(string template) =>
        Regex.Replace(template.Trim('/'), @"\{([^}:?]+)[^}]*\}", "{$1}");
}
