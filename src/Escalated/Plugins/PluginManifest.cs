using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Escalated.Plugins;

/// <summary>
/// A plugin's manifest, in the shape of the SDK's <c>definePlugin()</c> manifest:
/// what the admin screen lists, which pages, endpoints and webhooks it serves,
/// which hooks it handles, and what it adds to the shared screens. A native
/// plugin builds one in code; an SDK plugin's comes from the plugin runtime.
/// </summary>
public sealed class PluginManifest
{
    /// <summary>The plugin's name (<c>@acme/escalated-plugin-crm</c>). Its store and config are keyed by it.</summary>
    public required string Name { get; init; }

    /// <summary>The name the admin screen shows, when it differs from <see cref="Name"/>.</summary>
    public string? DisplayName { get; init; }

    public string Version { get; init; } = "1.0.0";
    public string? Description { get; init; }
    public string? Author { get; init; }
    public string? Homepage { get; init; }

    /// <summary>Admin pages, each rendered as <c>Escalated/Plugin/Page</c>.</summary>
    public IReadOnlyList<PluginPage> Pages { get; init; } = Array.Empty<PluginPage>();

    /// <summary>Components the plugin puts in named slots of the shared pages.</summary>
    public IReadOnlyList<PluginSlotComponent> Components { get; init; } = Array.Empty<PluginSlotComponent>();

    /// <summary>Dashboard widgets.</summary>
    public IReadOnlyList<PluginWidget> Widgets { get; init; } = Array.Empty<PluginWidget>();

    /// <summary>Endpoints, served at <c>{prefix}/api/plugins/{slug}{path}</c> behind the admin guard.</summary>
    public IReadOnlyList<PluginRoute> Endpoints { get; init; } = Array.Empty<PluginRoute>();

    /// <summary>Webhooks, served at <c>{prefix}/webhooks/plugins/{slug}{path}</c> without a guard.</summary>
    public IReadOnlyList<PluginRoute> Webhooks { get; init; } = Array.Empty<PluginRoute>();

    /// <summary>
    /// The actions the plugin handles. Null (the default for a native plugin) offers it
    /// every action and lets it decide; an SDK plugin's list comes from its manifest.
    /// </summary>
    public IReadOnlyList<string>? ActionHooks { get; init; }

    /// <summary>The filters the plugin applies, each with its priority (lowest runs first).</summary>
    public IReadOnlyList<PluginFilterHook> FilterHooks { get; init; } = Array.Empty<PluginFilterHook>();

    /// <summary>Cron schedules: five-field expressions, evaluated in UTC. A native plugin runs them through <see cref="IPluginCron"/>.</summary>
    public IReadOnlyList<string> CronSchedules { get; init; } = Array.Empty<string>();

    /// <summary>
    /// The contract's slug: no leading <c>@</c>, each <c>/</c> as <c>--</c>, anything
    /// else outside <c>[A-Za-z0-9_-]</c> as <c>-</c>. <c>@escalated-dev/plugin-slack</c>
    /// is <c>escalated-dev--plugin-slack</c>.
    /// </summary>
    public static string SlugFor(string name) =>
        Regex.Replace(name.TrimStart('@').Replace("/", "--", StringComparison.Ordinal), "[^A-Za-z0-9_-]", "-");

    /// <summary>Reads an SDK manifest as the plugin runtime reports it. Unusable entries are left out.</summary>
    public static PluginManifest FromJson(JsonObject manifest, string? fallbackName = null)
    {
        var name = String(manifest, "name") ?? fallbackName;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("An SDK plugin's manifest has no name.");
        }

        return new PluginManifest
        {
            Name = name,
            DisplayName = String(manifest, "display_name") ?? String(manifest, "displayName"),
            Version = Scalar(manifest["version"]) ?? "1.0.0",
            Description = String(manifest, "description"),
            Author = String(manifest, "author"),
            Homepage = String(manifest, "homepage") ?? String(manifest, "author_url"),
            Pages = Objects(manifest, "pages").Select(page => new PluginPage
            {
                Route = String(page, "route") ?? string.Empty,
                Component = String(page, "component") ?? string.Empty,
                Layout = String(page, "layout"),
                Capability = String(page, "capability"),
                Menu = page["menu"] is JsonObject menu && String(menu, "label") is { } label
                    ? new PluginMenu
                    {
                        Label = label,
                        Icon = String(menu, "icon"),
                        Position = Int(menu["position"]),
                        Section = String(menu, "section"),
                    }
                    : null,
            }).ToList(),
            Components = Objects(manifest, "components")
                .Where(c => String(c, "page") is not null && String(c, "slot") is not null && String(c, "component") is not null)
                .Select(c => new PluginSlotComponent
                {
                    Page = String(c, "page")!,
                    Slot = String(c, "slot")!,
                    Component = String(c, "component")!,
                    Props = c["props"] is JsonObject props ? (JsonObject)props.DeepClone() : null,
                    Order = Int(c["order"]),
                    Capability = String(c, "capability"),
                }).ToList(),
            Widgets = Objects(manifest, "widgets")
                .Where(w => String(w, "component") is not null)
                .Select(w => new PluginWidget
                {
                    Component = String(w, "component")!,
                    Label = String(w, "label"),
                    Size = String(w, "size"),
                    Order = Int(w["order"]),
                    Capability = String(w, "capability"),
                    HasBadge = w["hasBadge"] is JsonValue badge && badge.GetValueKind() == JsonValueKind.True,
                }).ToList(),
            Endpoints = Routes(manifest, "endpoints"),
            Webhooks = Routes(manifest, "webhooks"),
            ActionHooks = manifest["actionHooks"] is JsonArray actions
                ? actions.Select(a => a is JsonValue v && v.TryGetValue<string>(out var s) ? s : null).OfType<string>().ToList()
                : Array.Empty<string>(),
            FilterHooks = manifest["filterHooks"] is JsonArray filters
                ? filters.Select(f => f switch
                {
                    JsonValue v when v.TryGetValue<string>(out var hook) => new PluginFilterHook(hook),
                    JsonObject o when String(o, "hook") is { } hook => new PluginFilterHook(hook, Int(o["priority"]) ?? 10),
                    _ => null,
                }).OfType<PluginFilterHook>().ToList()
                : Array.Empty<PluginFilterHook>(),
            CronSchedules = manifest["cronSchedules"] is JsonArray schedules
                ? schedules.Select(c => c is JsonValue v && v.TryGetValue<string>(out var s) ? s : null).OfType<string>().ToList()
                : Array.Empty<string>(),
        };
    }

    private static IEnumerable<JsonObject> Objects(JsonObject manifest, string key) =>
        manifest[key] is JsonArray array ? array.OfType<JsonObject>() : Enumerable.Empty<JsonObject>();

    private static List<PluginRoute> Routes(JsonObject manifest, string key) =>
        Objects(manifest, key)
            .Where(r => String(r, "method") is not null && String(r, "path") is not null)
            .Select(r => new PluginRoute(String(r, "method")!, String(r, "path")!, String(r, "capability")))
            .ToList();

    private static string? String(JsonObject o, string key) =>
        o[key] is JsonValue value && value.TryGetValue<string>(out var s) && s.Length > 0 ? s : null;

    private static string? Scalar(JsonNode? node) => node switch
    {
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        JsonValue v when v.GetValueKind() == JsonValueKind.Number => v.ToJsonString(),
        _ => null,
    };

    private static int? Int(JsonNode? node) => node switch
    {
        JsonValue v when PluginJson.Number(v) is { } d => (int)d,
        JsonValue v when v.TryGetValue<string>(out var s) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) => i,
        _ => null,
    };
}

/// <summary>An admin page a plugin declares.</summary>
public sealed class PluginPage
{
    /// <summary>The path under <c>{prefix}/admin/plugins/{slug}/</c>. Leading slashes are dropped.</summary>
    public required string Route { get; init; }

    /// <summary>The frontend component the page renders, registered with <c>defineEscalatedPlugin</c>.</summary>
    public required string Component { get; init; }

    /// <summary>The layout; default <c>admin</c>.</summary>
    public string? Layout { get; init; }

    /// <summary>A capability the viewer must hold (see <see cref="PluginAccess"/>).</summary>
    public string? Capability { get; init; }

    /// <summary>A menu item linking to the page.</summary>
    public PluginMenu? Menu { get; init; }
}

public sealed class PluginMenu
{
    public required string Label { get; init; }
    public string? Icon { get; init; }

    /// <summary>Default 100.</summary>
    public int? Position { get; init; }

    /// <summary>Default <c>admin</c>; admin items are shown to admins only.</summary>
    public string? Section { get; init; }
}

/// <summary>A component in a slot of a shared page (<c>"{page}.{slot}"</c>).</summary>
public sealed class PluginSlotComponent
{
    public required string Page { get; init; }
    public required string Slot { get; init; }
    public required string Component { get; init; }
    public JsonObject? Props { get; init; }
    public int? Order { get; init; }
    public string? Capability { get; init; }
}

public sealed class PluginWidget
{
    public required string Component { get; init; }
    public string? Label { get; init; }

    /// <summary>Default <c>half</c>.</summary>
    public string? Size { get; init; }

    public int? Order { get; init; }
    public string? Capability { get; init; }

    /// <summary>A widget with a <c>badge</c> function: the runtime (SDK) or <see cref="IPluginWidgetBadges"/> (native) is asked for it.</summary>
    public bool HasBadge { get; init; }
}

/// <summary>An endpoint or webhook: a method and a path, which may hold <c>:param</c> segments.</summary>
public sealed record PluginRoute(string Method, string Path, string? Capability = null);

/// <summary>A filter a plugin applies; lower priorities run first.</summary>
public sealed record PluginFilterHook(string Hook, int Priority = 10);
