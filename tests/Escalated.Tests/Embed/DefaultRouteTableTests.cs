using System.Text.Encodings.Web;
using System.Text.Json;
using Escalated.Configuration;
using Escalated.Tests.Hosting;
using Escalated.Ui;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Escalated.Tests.Embed;

/// <summary>
/// Embedding the admin is optional and off by default (ADR 2026-11-05-embeddable-admin). With no
/// embed configuration the route table must be exactly what it was before the feature existed.
///
/// <para><c>Fixtures/embed/default-routes.json</c> was generated from <c>main</c> before the
/// change: every endpoint (route pattern, HTTP methods, route name, action, order, authorization
/// policies, filters and metadata types), MVC's global filters, and the <c>route()</c> name table.
/// The live table is compared with it. Regenerate it only for a deliberate route change, with
/// <c>ESCALATED_WRITE_ROUTE_SNAPSHOT=1 dotnet test --filter DefaultRouteTableTests</c>.</para>
/// </summary>
public class DefaultRouteTableTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    [Fact]
    public async Task RouteTableIsUnchangedWithoutEmbedConfiguration()
    {
        await using var host = await EscalatedTestHost.StartAsync();

        var live = JsonSerializer.Serialize(Snapshot(host), Json).ReplaceLineEndings("\n") + "\n";
        var path = FixturePath();

        if (Environment.GetEnvironmentVariable("ESCALATED_WRITE_ROUTE_SNAPSHOT") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, live);
        }

        var expected = (await File.ReadAllTextAsync(path)).ReplaceLineEndings("\n");
        Assert.Equal(expected, live);
    }

    [Fact]
    public async Task DefaultResponsesCarryNoEmbedPropsAndNoFramingChanges()
    {
        await using var host = await EscalatedTestHost.StartAsync(
            o => o.Ui.Enabled = true,
            null,
            app => app.Use((context, next) =>
            {
                context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
                return next(context);
            }));
        await host.GrantRoleAsync("admin-1", "escalated-admin");
        var admin = host.ClientFor("admin-1");

        var response = await admin.SendAsync(InertiaTestSupport.Visit(HttpMethod.Get, "/support/admin/tags"));
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var escalated = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement
            .GetProperty("props").GetProperty("escalated");
        Assert.False(escalated.TryGetProperty("embed", out _));
        Assert.False(escalated.TryGetProperty("navigation", out _));
        Assert.False(response.Headers.Contains("Content-Security-Policy"));
        Assert.Equal("SAMEORIGIN", response.Headers.GetValues("X-Frame-Options").Single());

        var html = await (await admin.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/support/admin/tags")
        {
            Headers = { { "Accept", "text/html" } },
        })).Content.ReadAsStringAsync();
        Assert.Contains("\"escalated.admin.tags.index\":\"/support/admin/tags\"", html);

        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await admin.GetAsync("/admin/support/tags")).StatusCode);
    }

    internal static Dictionary<string, object?> Snapshot(EscalatedTestHost host)
    {
        var endpoints = host.Endpoints
            .Select(Describe)
            .OrderBy(e => (string)e["pattern"]!, StringComparer.Ordinal)
            .ThenBy(e => string.Join(",", (IEnumerable<string>)e["methods"]!), StringComparer.Ordinal)
            .ThenBy(e => (string?)e["action"], StringComparer.Ordinal)
            .ToList();

        var mvc = host.Services.GetRequiredService<IOptions<MvcOptions>>().Value;
        var options = host.Services.GetRequiredService<IOptions<EscalatedOptions>>().Value;
        var routes = host.Services.GetRequiredService<IOptions<UiRouteOptions>>().Value;

        return new Dictionary<string, object?>
        {
            ["endpoints"] = endpoints,
            ["global_filters"] = mvc.Filters.Select(Filter).ToList(),
            ["route_names"] = new SortedDictionary<string, string>(
                UiRoutes.Resolve(routes.Routes, options.RoutePrefix), StringComparer.Ordinal),
        };
    }

    private static Dictionary<string, object?> Describe(RouteEndpoint endpoint)
    {
        var action = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>();
        var methods = endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods
            .OrderBy(m => m, StringComparer.Ordinal).ToList() ?? new List<string>();

        return new Dictionary<string, object?>
        {
            ["pattern"] = endpoint.RoutePattern.RawText ?? string.Empty,
            ["methods"] = methods,
            ["name"] = endpoint.Metadata.GetMetadata<RouteNameMetadata>()?.RouteName,
            ["action"] = action is null ? endpoint.DisplayName : $"{action.ControllerTypeInfo.FullName}.{action.ActionName}",
            ["order"] = endpoint.Order,
            ["authorize"] = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
                .Select(a => $"policy={a.Policy};roles={a.Roles};schemes={a.AuthenticationSchemes}").ToList(),
            ["allow_anonymous"] = endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null,
            ["filters"] = action?.FilterDescriptors.Select(f => $"{Filter(f.Filter)}@{f.Scope}").ToList() ?? new List<string>(),
            ["metadata"] = endpoint.Metadata.Select(m => m.GetType().FullName ?? m.GetType().Name)
                .Where(n => !IsCompilerMetadata(n))
                .Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList(),
        };
    }

    // Attributes the C# compiler puts on action methods differ between Debug and Release
    // builds (DebuggerStepThrough, async state machine and nullable attributes), so they
    // are not part of the route table.
    private static bool IsCompilerMetadata(string typeName) =>
        typeName.StartsWith("System.Diagnostics.", StringComparison.Ordinal)
        || typeName.StartsWith("System.Runtime.CompilerServices.", StringComparison.Ordinal);

    private static string Filter(IFilterMetadata filter) => filter switch
    {
        ServiceFilterAttribute service => $"service:{service.ServiceType.FullName}#{service.Order}",
        TypeFilterAttribute type => $"type:{type.ImplementationType.FullName}",
        IOrderedFilter ordered => $"{filter.GetType().FullName}#{ordered.Order}",
        _ => filter.GetType().FullName ?? filter.GetType().Name,
    };

    internal static string FixturePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Escalated.sln")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory!.FullName, "tests", "Escalated.Tests", "Fixtures", "embed", "default-routes.json");
    }
}
