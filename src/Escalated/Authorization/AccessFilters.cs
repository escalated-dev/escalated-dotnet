using System.Text.Json;
using Escalated.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace Escalated.Authorization;

/// <summary>
/// Answers 404 for the routes of an access feature whose switch is off (ADR
/// 2026-10-29-configurable-access-features), as if they did not exist, the way a disabled
/// widget's routes answer. Registered globally; it looks only at Escalated's own routes.
/// </summary>
public sealed class EnsureAccessFeaturesFilter : IAsyncResourceFilter
{
    /// <summary>Each feature's route templates, as <see cref="RoutePermissions"/> reads them.</summary>
    internal static readonly Dictionary<string, string[]> Routes = new()
    {
        [AccessFeatures.RolesPermissionsKey] = new[] { "/P/admin/roles", "/P/admin/roles/**" },
        [AccessFeatures.TwoFactorKey] = new[] { "/P/admin/settings/two-factor", "/P/admin/settings/two-factor/**", "/P/two-factor/**" },
    };

    private readonly AccessFeatures _features;
    private readonly IOptions<EscalatedOptions> _options;

    public EnsureAccessFeaturesFilter(AccessFeatures features, IOptions<EscalatedOptions> options)
    {
        _features = features;
        _options = options;
    }

    internal static string? FeatureOf(string template)
    {
        foreach (var (feature, patterns) in Routes)
        {
            if (patterns.Any(pattern => RoutePermissions.Regex(pattern).IsMatch(template)))
            {
                return feature;
            }
        }

        return null;
    }

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var template = RoutePermissions.Template(context.ActionDescriptor.AttributeRouteInfo?.Template, _options.Value.RoutePrefix);
        var feature = template is null ? null : FeatureOf(template);
        if (feature is not null && !_features.Enabled(feature))
        {
            context.Result = new NotFoundObjectResult(new { message = "Not Found" });
            return;
        }

        await next();
    }
}

/// <summary>
/// Requires each staff route's permission while roles &amp; permissions is on (ADR
/// 2026-10-29-configurable-access-features), Laravel's <c>EnforceRoutePermissions</c>. It runs
/// after the host's authorization middleware has applied the admin and agent policies, so both
/// must pass. A staff route missing from <see cref="RoutePermissions"/> is refused (fail closed);
/// lacking a permission answers 403. Status, bulk and reply routes decide part of what they need
/// from the request body, which is buffered and rewound for the action.
/// </summary>
public sealed class EnforceRoutePermissionsFilter : IAsyncResourceFilter
{
    private readonly AccessControl _access;
    private readonly IOptions<EscalatedOptions> _options;

    public EnforceRoutePermissionsFilter(AccessControl access, IOptions<EscalatedOptions> options)
    {
        _access = access;
        _options = options;
    }

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var template = RoutePermissions.Template(context.ActionDescriptor.AttributeRouteInfo?.Template, _options.Value.RoutePrefix);
        if (!_access.Enforced || template is null || !RoutePermissions.IsStaffRoute(template))
        {
            await next();
            return;
        }

        var request = context.HttpContext.Request;
        var mapped = RoutePermissions.Mapped(request.Method, template);
        if (mapped is null)
        {
            Forbid(context);
            return;
        }

        var input = RoutePermissions.DecidedByBody(template, mapped)
            ? await InputAsync(request)
            : new Dictionary<string, string?>();
        var required = RoutePermissions.Required(request.Method, template, input);
        var userId = EscalatedUser.ResolveId(context.HttpContext.User, _options.Value);
        if (required is null || !await _access.AllowsAsync(userId, required, context.HttpContext.RequestAborted))
        {
            Forbid(context);
            return;
        }

        await next();
    }

    private static void Forbid(ResourceExecutingContext context)
    {
        context.Result = new ObjectResult(new { message = "This action is unauthorized." }) { StatusCode = 403 };
    }

    /// <summary>The submitted values (a JSON object or form fields), leaving the body readable for the action.</summary>
    private static async Task<Dictionary<string, string?>> InputAsync(HttpRequest request)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync();
            foreach (var (key, value) in form)
            {
                values[key] = value.ToString();
            }

            return values;
        }

        if (request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) != true)
        {
            return values;
        }

        request.EnableBuffering();
        try
        {
            using var document = await JsonDocument.ParseAsync(request.Body);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    values[property.Name] = property.Value.ValueKind switch
                    {
                        JsonValueKind.String => property.Value.GetString(),
                        JsonValueKind.Null => null,
                        _ => property.Value.GetRawText(),
                    };
                }
            }
        }
        catch (JsonException)
        {
            // The action reports a malformed body; nothing here depends on it.
        }
        finally
        {
            request.Body.Position = 0;
        }

        return values;
    }
}
