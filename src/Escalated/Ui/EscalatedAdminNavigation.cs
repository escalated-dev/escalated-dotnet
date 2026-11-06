using System.Security.Claims;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Escalated.Authorization;
using Escalated.Configuration;
using Escalated.Plugins;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Escalated.Ui;

/// <summary>One entry of Escalated's admin menu (ADR 2026-11-05-embeddable-admin).</summary>
public sealed class AdminNavigationItem
{
    /// <summary>Stable id: the core screen's key, or <c>plugin:&lt;slug&gt;:&lt;n&gt;</c>.</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>English label, as in the standalone sidebar.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>The <c>route()</c> name, or null for plugin items.</summary>
    public string? Route { get; init; }

    /// <summary>Absolute path of the screen.</summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>Icon key (the frontend's <c>escalatedNavIcons</c>); plugin items pass their own.</summary>
    public string? Icon { get; init; }

    /// <summary>Sort order, ascending, as in the standalone sidebar.</summary>
    public double Position { get; init; }

    /// <summary>The current request's path is <see cref="Url"/> or below it.</summary>
    public bool Active { get; init; }

    public object? Badge { get; init; }

    /// <summary>The plugin slug for plugin items, else null.</summary>
    public string? Plugin { get; init; }

    /// <summary>Plugin submenu items (same shape without children).</summary>
    public List<AdminNavigationItem> Children { get; init; } = new();
}

/// <summary>
/// Escalated's admin menu as data, so a host can list it in its own sidebar (ADR
/// 2026-11-05-embeddable-admin). Shared as <c>escalated.navigation</c> while the admin is
/// embedded; call <see cref="ForAsync"/> (or <c>HttpContext.EscalatedAdminNavigationAsync()</c>)
/// to render it in a Razor layout. It works whether or not embedding is on: the URLs follow
/// wherever the admin is mounted.
///
/// <para>Filtered so a host sidebar never links to a screen that would answer 403 or 404: empty
/// for a user who fails the <c>escalated-admin</c> policy (unless the host replaced that gate);
/// screens whose route is not served; Roles while roles &amp; permissions is off; and, while it is
/// on, each screen whose route needs a permission the user lacks. Active plugins' admin menu
/// items are merged in, and everything is sorted by position.</para>
/// </summary>
public sealed class EscalatedAdminNavigation
{
    /// <summary>Core items: key (also the icon), label, route name, path below the admin, position.</summary>
    internal static readonly (string Key, string Label, string Route, string Path, double Position)[] Items =
    {
        ("reports", "Reports", "escalated.admin.reports", "reports", 10),
        ("tickets", "Tickets", "escalated.admin.tickets.index", "tickets", 20),
        ("departments", "Departments", "escalated.admin.departments.index", "departments", 30),
        ("sla-policies", "SLA Policies", "escalated.admin.sla-policies.index", "sla-policies", 40),
        ("knowledge-base", "Knowledge Base", "escalated.admin.kb-articles.index", "kb-articles", 45),
        ("escalation-rules", "Escalation Rules", "escalated.admin.escalation-rules.index", "escalation-rules", 50),
        ("workflows", "Workflows", "escalated.admin.workflows.index", "workflows", 53),
        ("automations", "Automations", "escalated.admin.automations.index", "automations", 53.5),
        ("webhooks", "Webhooks", "escalated.admin.webhooks.index", "webhooks", 54),
        ("tags", "Tags", "escalated.admin.tags.index", "tags", 60),
        ("custom-fields", "Custom Fields", "escalated.admin.custom-fields.index", "custom-fields", 62),
        ("statuses", "Statuses", "escalated.admin.statuses.index", "statuses", 64),
        ("canned-responses", "Canned Responses", "escalated.admin.canned-responses.index", "canned-responses", 70),
        ("macros", "Macros", "escalated.admin.macros.index", "macros", 80),
        ("newsletters", "Newsletters", "escalated.admin.newsletters.index", NewslettersPath, 81),
        ("skills", "Skills", "escalated.admin.skills.index", "skills", 82),
        ("users", "Users", "escalated.admin.users.index", "users", 83),
        ("roles", "Roles", "escalated.admin.roles.index", "roles", 84),
        ("capacity", "Capacity", "escalated.admin.capacity.index", "capacity", 85),
        ("business-hours", "Business Hours", "escalated.admin.business-hours.index", "business-hours", 86),
        ("audit-log", "Audit Log", "escalated.admin.audit-log", "audit-log", 88),
        ("api-tokens", "API Tokens", "escalated.admin.api-tokens.index", "api-tokens", 90),
        ("custom-objects", "Custom Objects", "escalated.admin.custom-objects.index", "custom-objects", 92),
        ("plugins", "Plugins", "escalated.admin.plugins.index", "plugins", 95),
        ("import", "Import", "escalated.admin.import.index", "import", 97),
        ("settings", "Settings", "escalated.admin.settings", "settings", 100),
    };

    /// <summary>The newsletter admin is not under the admin prefix and never moves.</summary>
    private const string NewslettersPath = "/admin/newsletters";

    private readonly IServiceProvider _services;
    private readonly IAuthorizationService _authorization;
    private readonly AccessFeatures _features;
    private readonly AccessControl _access;
    private readonly EscalatedOptions _options;

    public EscalatedAdminNavigation(IServiceProvider services, IAuthorizationService authorization, AccessFeatures features,
        AccessControl access, IOptions<EscalatedOptions> options)
    {
        _services = services;
        _authorization = authorization;
        _features = features;
        _access = access;
        _options = options.Value;
    }

    /// <summary>The admin menu for <paramref name="user"/> (default: the request's user), with <c>active</c> from the request path.</summary>
    public async Task<IReadOnlyList<AdminNavigationItem>> ForAsync(ClaimsPrincipal? user, HttpContext? context, CancellationToken ct = default)
    {
        user ??= context?.User;
        var userId = EscalatedUser.ResolveId(user, _options);
        if (user is null || userId is null)
        {
            return Array.Empty<AdminNavigationItem>();
        }

        if (!_options.Ui.Embed.ReplaceAdminGate && !(await _authorization.AuthorizeAsync(user, EscalatedPolicies.Admin)).Succeeded)
        {
            return Array.Empty<AdminNavigationItem>();
        }

        var path = context?.Request.Path.Value ?? string.Empty;
        var served = ServedPages();
        var admin = EscalatedEmbed.AdminBase(_options);
        var granted = _access.Enforced ? await _access.PermissionsOfAsync(userId, ct) : null;
        var items = new List<AdminNavigationItem>();

        foreach (var (key, label, route, relative, position) in Items)
        {
            var newsletters = relative == NewslettersPath;
            var template = newsletters ? NewslettersPath : "/P/admin/" + relative;
            if (!served.Contains(template)
                || (newsletters && !_options.EnableNewsletters)
                || (key == "roles" && !_features.RolesPermissions))
            {
                continue;
            }

            if (granted is not null)
            {
                var required = RoutePermissions.Required("GET", template, new Dictionary<string, string?>());
                if (required is null || !required.All(granted.Contains))
                {
                    continue;
                }
            }

            var url = newsletters ? NewslettersPath : admin + "/" + relative;
            items.Add(Item(key, label, route, url, key, position, path));
        }

        var index = 0;
        foreach (var plugin in await PluginItemsAsync(user, ct))
        {
            var url = plugin["route"]?.GetValue<string>();
            if (string.IsNullOrEmpty(url))
            {
                continue;
            }

            var slug = plugin["plugin"]?.GetValue<string>();
            var item = Item($"plugin:{slug ?? "native"}:{index++}", plugin["label"]?.GetValue<string>() ?? string.Empty, null, url,
                plugin["icon"] is JsonValue icon && icon.TryGetValue<string>(out var name) ? name : null,
                plugin["position"] is JsonValue p && double.TryParse(p.ToJsonString(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var position) ? position : 100, path);
            items.Add(new AdminNavigationItem
            {
                Key = item.Key,
                Label = item.Label,
                Route = null,
                Url = item.Url,
                Icon = item.Icon,
                Position = item.Position,
                Active = item.Active,
                Badge = null,
                Plugin = slug,
            });
        }

        // A stable sort: equal positions keep their order, as the standalone sidebar does.
        return items.Select((item, i) => (item, i)).OrderBy(x => x.item.Position).ThenBy(x => x.i).Select(x => x.item).ToList();
    }

    private static AdminNavigationItem Item(string key, string label, string? route, string url, string? icon, double position, string path)
    {
        var target = url.Split('?', 2)[0];
        var active = string.Equals(path, target, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(target.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase);
        return new AdminNavigationItem
        {
            Key = key,
            Label = label,
            Route = route,
            Url = url,
            Icon = icon,
            Position = position,
            Active = active,
        };
    }

    /// <summary>The GET routes this app serves, as <see cref="RoutePermissions"/> templates with parameters as <c>*</c>.</summary>
    private HashSet<string> ServedPages()
    {
        var served = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_services.GetService<EndpointDataSource>() is not { } source)
        {
            return served;
        }

        foreach (var endpoint in source.Endpoints.OfType<RouteEndpoint>())
        {
            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods;
            if (methods is not null && !methods.Contains("GET", StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var raw = endpoint.Metadata.GetMetadata<EscalatedEmbeddedAdminMetadata>()?.StandaloneTemplate ?? endpoint.RoutePattern.RawText;
            if (RoutePermissions.Template(raw, _options.RoutePrefix) is { } template)
            {
                served.Add(template);
            }
        }

        return served;
    }

    private async Task<List<JsonObject>> PluginItemsAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        if (_services.GetService<PluginRegistry>() is not { } plugins)
        {
            return new List<JsonObject>();
        }

        var extensions = await plugins.UiExtensionsAsync(user, ct);
        return extensions["menuItems"] is JsonArray menu
            ? menu.OfType<JsonObject>().Where(item => (item["section"]?.GetValue<string>() ?? "admin") == "admin").ToList()
            : new List<JsonObject>();
    }

    /// <summary>
    /// The <c>route()</c> name of an embedded admin endpoint: the first name in
    /// <see cref="UiRouteOptions"/> whose path has the endpoint's standalone template.
    /// </summary>
    internal static string? RouteName(EscalatedEmbeddedAdminMetadata embedded, IReadOnlyDictionary<string, string> routes, string routePrefix)
    {
        var target = Shape(RoutePermissions.Template(embedded.StandaloneTemplate, routePrefix));
        foreach (var (name, path) in routes)
        {
            if (Shape(RoutePermissions.Template(path.Replace("{prefix}", routePrefix.Trim('/')), routePrefix)) == target)
            {
                return name;
            }
        }

        return null;
    }

    private static string? Shape(string? template) =>
        template is null ? null : Regex.Replace(template, @"\{[^}]*\}", "*").ToLowerInvariant();
}

public static class EscalatedAdminNavigationExtensions
{
    /// <summary>
    /// Escalated's admin menu for the signed-in user (or <paramref name="user"/>), for a
    /// server-rendered admin layout: <c>@foreach (var item in await Context.EscalatedAdminNavigationAsync())</c>.
    /// </summary>
    public static Task<IReadOnlyList<AdminNavigationItem>> EscalatedAdminNavigationAsync(this HttpContext context, ClaimsPrincipal? user = null) =>
        context.RequestServices.GetRequiredService<EscalatedAdminNavigation>().ForAsync(user ?? context.User, context, context.RequestAborted);
}
