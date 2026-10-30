using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace Escalated.Authorization;

/// <summary>
/// The permission each admin, agent and staff API route requires while roles &amp; permissions is
/// enforced (ADR 2026-10-29-configurable-access-features): escalated-laravel's
/// <c>src/Support/RoutePermissions.php</c>, keyed by HTTP method and route template instead of a
/// route name.
///
/// <para>Templates are compared with the route prefix written <c>/P</c> (so
/// <c>support/admin/tickets/{id:int}</c> is <c>/P/admin/tickets/{id:int}</c>). Entries are tried in
/// order and the first match wins. In a pattern <c>*</c> is one segment (a parameter such as
/// <c>{id:int}</c> is one), <c>**</c> any number, and <c>{a,b}</c> either word. A staff route that
/// matches nothing is refused, and a test enumerates the endpoint table so a new route cannot ship
/// without an entry.</para>
///
/// <para>Every slug is one escalated-laravel's <c>PermissionSeeder</c> seeds. An empty list means
/// "staff, no particular permission": personal screens, and plugin pages and endpoints, which the
/// plugin guards with its own capability.</para>
/// </summary>
public static partial class RoutePermissions
{
    public sealed record Entry(string Methods, string Pattern, string[] Slugs);

    /// <summary>Statuses whose change needs <c>ticket.close</c> as well as <c>ticket.edit</c>.</summary>
    public static readonly HashSet<string> ClosingStatuses = new(StringComparer.Ordinal) { "resolved", "closed", "reopened" };

    /// <summary>Bulk actions and the permission each needs.</summary>
    public static readonly Dictionary<string, string[]> BulkActions = new(StringComparer.Ordinal)
    {
        ["status"] = new[] { "ticket.edit" },
        ["priority"] = new[] { "ticket.edit" },
        ["assign"] = new[] { "ticket.assign" },
        ["tags"] = new[] { "ticket.edit" },
        ["department"] = new[] { "ticket.edit" },
        ["delete"] = new[] { "ticket.delete" },
    };

    /// <summary>The template prefixes whose routes are staff routes, and so must be mapped.</summary>
    public static readonly string[] StaffPrefixes = { "/P/admin", "/P/agent", "/P/api", "/admin/newsletters" };

    private static readonly ConcurrentDictionary<string, Regex> Compiled = new();

    public static IReadOnlyList<Entry> Entries { get; } = Build();

    /// <summary>
    /// A route template as the map reads it: leading slash, the route prefix written <c>/P</c>.
    /// Null for an action without an attribute route.
    /// </summary>
    public static string? Template(string? template, string routePrefix)
    {
        if (template is null)
        {
            return null;
        }

        var path = "/" + template.Trim('/');
        var prefix = "/" + routePrefix.Trim('/');
        if (path.Equals(prefix, StringComparison.OrdinalIgnoreCase) || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
        {
            path = "/P" + path[prefix.Length..];
        }

        return path;
    }

    /// <summary>Whether the template is a staff route: admin, agent, the staff API, newsletters.</summary>
    public static bool IsStaffRoute(string template)
    {
        // The mobile customer and guest APIs and the host-auth endpoints are not staff routes.
        if (template.StartsWith("/P/api/v1/mobile", StringComparison.Ordinal)
            || template.StartsWith("/P/api/v1/auth", StringComparison.Ordinal))
        {
            return false;
        }

        return StaffPrefixes.Any(prefix => template == prefix || template.StartsWith(prefix + "/", StringComparison.Ordinal));
    }

    /// <summary>The slugs a route needs before looking at the body, or null when it is unmapped.</summary>
    public static List<string>? Mapped(string method, string template)
    {
        var verb = method.Equals("HEAD", StringComparison.OrdinalIgnoreCase) ? "GET" : method.ToUpperInvariant();
        foreach (var entry in Entries)
        {
            if (entry.Methods != "*" && !entry.Methods.Split('|').Contains(verb))
            {
                continue;
            }

            if (Regex(entry.Pattern).IsMatch(template))
            {
                return entry.Slugs.ToList();
            }
        }

        return null;
    }

    /// <summary>Whether part of what the route needs is decided by the request body.</summary>
    public static bool DecidedByBody(string template, List<string> mapped) =>
        mapped.Contains("ticket.edit") || template.EndsWith("/bulk", StringComparison.Ordinal)
        || template.EndsWith("/replies", StringComparison.Ordinal) || template.EndsWith("/reply", StringComparison.Ordinal);

    /// <summary>
    /// The slugs a request needs, or null when unmapped (the caller refuses it). A status change to
    /// resolved/closed/reopened also needs <c>ticket.close</c>; a bulk action needs its own
    /// permission; a reply marked internal needs <c>reply.create_internal</c>.
    /// </summary>
    public static List<string>? Required(string method, string template, IReadOnlyDictionary<string, string?> input)
    {
        var required = Mapped(method, template);
        if (required is null)
        {
            return null;
        }

        if (template.EndsWith("/bulk", StringComparison.Ordinal))
        {
            var action = input.TryGetValue("action", out var a) ? a ?? string.Empty : string.Empty;
            if (BulkActions.TryGetValue(action, out var slugs))
            {
                required.AddRange(slugs);
            }

            if (action == "status")
            {
                required.AddRange(Closing(input.TryGetValue("value", out var v) ? v : null));
            }
        }
        else if ((template.EndsWith("/replies", StringComparison.Ordinal) || template.EndsWith("/reply", StringComparison.Ordinal))
                 && (Truthy(input, "is_internal") || Truthy(input, "isInternal") || Truthy(input, "internal")))
        {
            required = new List<string> { "reply.create_internal" };
        }
        else if (required.Contains("ticket.edit"))
        {
            required.AddRange(Closing(input.TryGetValue("status", out var s) ? s : null));
        }

        return required.Distinct(StringComparer.Ordinal).ToList();
    }

    private static IEnumerable<string> Closing(string? status) =>
        status is not null && ClosingStatuses.Contains(status.Trim().ToLowerInvariant()) ? new[] { "ticket.close" } : Array.Empty<string>();

    private static bool Truthy(IReadOnlyDictionary<string, string?> input, string key) =>
        input.TryGetValue(key, out var value) && value is not null && value.Trim().ToLowerInvariant() is "true" or "1";

    internal static Regex Regex(string pattern) => Compiled.GetOrAdd(pattern, Compile);

    private static Regex Compile(string pattern)
    {
        var source = new StringBuilder("^");
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c == '*' && i + 1 < pattern.Length && pattern[i + 1] == '*')
            {
                source.Append(".+");
                i++;
            }
            else if (c == '*')
            {
                source.Append("[^/]+");
            }
            else if (c == '{')
            {
                var end = pattern.IndexOf('}', i);
                source.Append("(?:").Append(string.Join("|", pattern[(i + 1)..end].Split(',').Select(System.Text.RegularExpressions.Regex.Escape))).Append(')');
                i = end;
            }
            else
            {
                source.Append(System.Text.RegularExpressions.Regex.Escape(c.ToString()));
            }
        }

        return new Regex(source.Append('$').ToString(), RegexOptions.CultureInvariant);
    }

    private static Entry E(string methods, string pattern, params string[] slugs) => new(methods, pattern, slugs);

    /// <summary>Index and reads need <c>{group}.view</c>; the forms and every write <c>{group}.manage</c>.</summary>
    private static IEnumerable<Entry> Resource(string basePath, string group)
    {
        var view = group + ".view";
        var manage = group + ".manage";
        yield return E("GET", basePath + "/create", manage);
        yield return E("GET", basePath + "/*/edit", manage);
        yield return E("GET", basePath, view);
        yield return E("GET", basePath + "/**", view);
        yield return E("*", basePath, manage);
        yield return E("*", basePath + "/**", manage);
    }
}
