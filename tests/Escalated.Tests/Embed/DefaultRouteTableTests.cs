using System.Text.Encodings.Web;
using System.Text.Json;
using Escalated.Configuration;
using Escalated.Tests.Hosting;
using Escalated.Ui;
using Microsoft.AspNetCore.Authorization;
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
                .Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList(),
        };
    }

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
