using System.Text.Json.Nodes;

namespace Escalated.Plugins;

/// <summary>
/// A native plugin: .NET code registered in the host's container and run in the
/// host process. It is the .NET shape of an SDK <c>definePlugin()</c> object, and
/// is listed in Admin &gt; Plugins as <c>registered</c>. Register it with
/// <c>services.AddEscalatedPlugin&lt;MyPlugin&gt;()</c> (or as any
/// <see cref="IEscalatedPlugin"/> service), and extend <see cref="EscalatedPlugin"/>
/// to override only what the plugin needs.
///
/// <para>Nothing here runs while the plugin is inactive. A handler that throws is
/// logged with the plugin's name; it never fails the request that caused it,
/// except an endpoint's, which answers 500 with a generic message.</para>
/// </summary>
public interface IEscalatedPlugin
{
    /// <summary>The plugin's manifest: name, pages, endpoints, webhooks, hooks and UI extensions.</summary>
    PluginManifest Manifest { get; }

    /// <summary>After the plugin is activated (its row is already active).</summary>
    Task OnActivateAsync(PluginContext ctx, CancellationToken ct);

    /// <summary>Before the plugin's row is deactivated.</summary>
    Task OnDeactivateAsync(PluginContext ctx, CancellationToken ct);

    /// <summary>
    /// An action: a canonical event (<c>ticket.created</c>, ...), a lifecycle action
    /// (<c>plugin.activated</c>, ...), or one a plugin emitted. Offered for every action
    /// unless the manifest lists <see cref="PluginManifest.ActionHooks"/>.
    /// </summary>
    Task HandleActionAsync(string hook, JsonNode? payload, PluginContext ctx, CancellationToken ct);

    /// <summary>A filter the manifest declares in <see cref="PluginManifest.FilterHooks"/>.</summary>
    Task<JsonNode?> ApplyFilterAsync(string hook, JsonNode? value, PluginContext ctx, CancellationToken ct);

    /// <summary>
    /// An endpoint the manifest declares. <see cref="PluginHttpRequest.Path"/> is the
    /// manifest path. Return any JSON-serializable value (200), or a
    /// <see cref="PluginHttpResponse"/> to set the status, format and headers.
    /// </summary>
    Task<object?> HandleEndpointAsync(PluginHttpRequest request, PluginContext ctx, CancellationToken ct);

    /// <summary>A webhook the manifest declares, as <see cref="HandleEndpointAsync"/>. The plugin authenticates the sender.</summary>
    Task<object?> HandleWebhookAsync(PluginHttpRequest request, PluginContext ctx, CancellationToken ct);
}

/// <summary>
/// Optional for a native plugin: its cron schedules, the native form of the SDK's
/// <c>cron</c> map. The schedules are the manifest's
/// <see cref="PluginManifest.CronSchedules"/> (five-field expressions, UTC). Once a
/// minute the host calls <see cref="RunCronAsync"/> with each schedule that is due,
/// for an active plugin only, as no user, within 30 s. A schedule that throws or
/// runs over is logged and never stops the others; one the host cannot read is
/// logged and skipped.
/// </summary>
public interface IPluginCron
{
    Task RunCronAsync(string schedule, PluginContext ctx, CancellationToken ct);
}

/// <summary>
/// Optional for a native plugin: live counts on its dashboard widgets, the native
/// form of an SDK widget's <c>badge</c> function. For each widget whose
/// <see cref="PluginWidget.HasBadge"/> is set, while <c>dashboardWidgets</c> is built
/// for a viewer, the host calls <see cref="BadgeAsync"/> with the widget's component,
/// as that viewer, within 5 s. Return a number, a string or null; anything else, a
/// throw or a timeout is <c>badge: null</c>.
/// </summary>
public interface IPluginWidgetBadges
{
    Task<object?> BadgeAsync(string component, PluginContext ctx, CancellationToken ct);
}

/// <summary>A native plugin that does nothing until a member is overridden.</summary>
public abstract class EscalatedPlugin : IEscalatedPlugin
{
    public abstract PluginManifest Manifest { get; }

    public virtual Task OnActivateAsync(PluginContext ctx, CancellationToken ct) => Task.CompletedTask;

    public virtual Task OnDeactivateAsync(PluginContext ctx, CancellationToken ct) => Task.CompletedTask;

    public virtual Task HandleActionAsync(string hook, JsonNode? payload, PluginContext ctx, CancellationToken ct) => Task.CompletedTask;

    public virtual Task<JsonNode?> ApplyFilterAsync(string hook, JsonNode? value, PluginContext ctx, CancellationToken ct) =>
        Task.FromResult(value);

    public virtual Task<object?> HandleEndpointAsync(PluginHttpRequest request, PluginContext ctx, CancellationToken ct) =>
        throw new NotSupportedException($"{GetType().Name} declares {request.Method} {request.Path} but does not handle it.");

    public virtual Task<object?> HandleWebhookAsync(PluginHttpRequest request, PluginContext ctx, CancellationToken ct) =>
        throw new NotSupportedException($"{GetType().Name} declares webhook {request.Method} {request.Path} but does not handle it.");
}

/// <summary>Where a plugin came from, as the admin screen's <c>source</c> reads it.</summary>
public static class PluginSources
{
    /// <summary>Code-registered in the host's container (a native plugin). Uninstall keeps it listed, inactive.</summary>
    public const string Registered = "registered";

    /// <summary>Managed by a package manager (an SDK plugin installed with npm). Uninstall is refused.</summary>
    public const string Composer = "composer";
}

/// <summary>One plugin the host knows about, native or SDK.</summary>
public sealed class RegisteredPlugin
{
    private RegisteredPlugin(PluginManifest manifest, string source, IEscalatedPlugin? native)
    {
        Manifest = manifest;
        Name = manifest.Name;
        Slug = PluginManifest.SlugFor(manifest.Name);
        Source = source;
        Native = native;
        Pages = manifest.Pages
            .Select(page => (page, route: page.Route.TrimStart('/')))
            .Where(p => p.route.Length > 0 && p.page.Component.Length > 0)
            .Select(p => new PluginPage
            {
                Route = p.route,
                Component = p.page.Component,
                Layout = string.IsNullOrEmpty(p.page.Layout) ? "admin" : p.page.Layout,
                Capability = p.page.Capability,
                Menu = p.page.Menu,
            })
            .ToList();
        SkippedPages = manifest.Pages.Count - Pages.Count;
        Endpoints = Normalize(manifest.Endpoints);
        Webhooks = Normalize(manifest.Webhooks);
    }

    public string Name { get; }
    public string Slug { get; }
    public string Source { get; }
    public PluginManifest Manifest { get; }

    /// <summary>The native plugin, or null for an SDK plugin served by the runtime.</summary>
    public IEscalatedPlugin? Native { get; }

    public bool IsNative => Native is not null;

    /// <summary>The pages that can be served: a route (leading slashes dropped) and a component; layout defaulted.</summary>
    public IReadOnlyList<PluginPage> Pages { get; }

    /// <summary>Pages left out for an empty route or component.</summary>
    public int SkippedPages { get; }

    /// <summary>Endpoints with upper-case methods and a leading slash.</summary>
    public IReadOnlyList<PluginRoute> Endpoints { get; }

    public IReadOnlyList<PluginRoute> Webhooks { get; }

    public static RegisteredPlugin ForNative(IEscalatedPlugin plugin)
    {
        var manifest = plugin.Manifest ?? throw new InvalidOperationException($"{plugin.GetType().Name} has no manifest.");
        if (string.IsNullOrWhiteSpace(manifest.Name))
        {
            throw new InvalidOperationException($"{plugin.GetType().Name} has no manifest name.");
        }

        return new RegisteredPlugin(manifest, PluginSources.Registered, plugin);
    }

    public static RegisteredPlugin ForSdk(PluginManifest manifest) => new(manifest, PluginSources.Composer, null);

    public PluginPage? Page(string route)
    {
        var trimmed = route.TrimStart('/');
        return Pages.FirstOrDefault(page => page.Route == trimmed);
    }

    /// <summary>Whether the plugin handles <paramref name="hook"/>. A native plugin without a list handles every action.</summary>
    public bool HandlesAction(string hook) => Manifest.ActionHooks is null ? IsNative : Manifest.ActionHooks.Contains(hook);

    private static List<PluginRoute> Normalize(IEnumerable<PluginRoute> routes) =>
        routes.Where(r => !string.IsNullOrWhiteSpace(r.Method) && !string.IsNullOrWhiteSpace(r.Path))
            .Select(r => r with { Method = r.Method.ToUpperInvariant(), Path = "/" + r.Path.TrimStart('/') })
            .ToList();
}
