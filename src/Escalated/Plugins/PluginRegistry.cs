using System.Security.Claims;
using System.Text.Json.Nodes;
using Escalated.Configuration;
using Escalated.Data;
using Escalated.Events;
using Escalated.Plugins.Bridge;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PluginRow = Escalated.Models.Plugin;

namespace Escalated.Plugins;

/// <summary>What <see cref="PluginRegistry.UninstallAsync"/> did.</summary>
public enum PluginUninstallResult
{
    Uninstalled,
    Unknown,
    Refused,
}

/// <summary>
/// Every plugin this host runs, native and SDK, with its activation state in
/// <c>escalated_plugins</c>, its lifecycle, and the dispatch of actions, filters,
/// endpoints and webhooks to the active ones (the plugin contract, with
/// escalated-nestjs as the reference).
///
/// <para>Native plugins are <see cref="IEscalatedPlugin"/> services and run in this
/// process. SDK plugins are whatever the plugin runtime loaded; the runtime is only
/// started when an SDK plugin is needed. An inactive plugin is never called.</para>
/// </summary>
public sealed class PluginRegistry
{
    public static readonly IReadOnlyList<string> LifecycleActions = new[] { "plugin.activated", "plugin.deactivated", "plugin.uninstalling" };

    private readonly IEnumerable<IEscalatedPlugin> _nativeServices;
    private readonly PluginBridge _bridge;
    private readonly EscalatedDbContext _db;
    private readonly PluginContextHandler _contexts;
    private readonly PluginAccess _access;
    private readonly PluginEventRelay _relay;
    private readonly ILogger<PluginRegistry> _logger;
    private readonly EscalatedOptions _options;
    private Dictionary<string, RegisteredPlugin>? _natives;

    public PluginRegistry(IEnumerable<IEscalatedPlugin> nativeServices, PluginBridge bridge, EscalatedDbContext db,
        PluginContextHandler contexts, PluginAccess access, PluginEventRelay relay, ILogger<PluginRegistry> logger,
        IOptions<EscalatedOptions> options)
    {
        _nativeServices = nativeServices;
        _bridge = bridge;
        _db = db;
        _contexts = contexts;
        _access = access;
        _relay = relay;
        _logger = logger;
        _options = options.Value;
    }

    /// <summary>The native plugins by slug. Two with one slug is a configuration error.</summary>
    public IReadOnlyDictionary<string, RegisteredPlugin> Natives()
    {
        if (_natives is not null) return _natives;

        var natives = new Dictionary<string, RegisteredPlugin>(StringComparer.Ordinal);
        foreach (var service in _nativeServices)
        {
            var plugin = RegisteredPlugin.ForNative(service);
            Register(natives, plugin);
        }

        return _natives = natives;
    }

    /// <summary>Every registered plugin by slug, natives first. Starts the runtime when the bridge is on.</summary>
    public async Task<IReadOnlyDictionary<string, RegisteredPlugin>> AllAsync(CancellationToken ct = default)
    {
        var all = new Dictionary<string, RegisteredPlugin>(Natives(), StringComparer.Ordinal);
        foreach (var manifest in (await _bridge.ManifestsAsync(ct)).Values)
        {
            Register(all, RegisteredPlugin.ForSdk(manifest));
        }

        return all;
    }

    public async Task<RegisteredPlugin?> FindAsync(string slug, CancellationToken ct = default) =>
        Natives().TryGetValue(slug, out var native) ? native : (await AllAsync(ct)).GetValueOrDefault(slug);

    /// <summary>The plugin with this slug when it is active, else null.</summary>
    public async Task<RegisteredPlugin?> ActiveAsync(string slug, CancellationToken ct = default) =>
        (await ActiveSlugsAsync(ct)).Contains(slug) ? await FindAsync(slug, ct) : null;

    /// <summary>Active plugins, natives first. Starts the runtime only when an active slug is not a native plugin.</summary>
    public async Task<IReadOnlyList<RegisteredPlugin>> ActivePluginsAsync(CancellationToken ct = default)
    {
        var slugs = await ActiveSlugsAsync(ct);
        if (slugs.Count == 0) return Array.Empty<RegisteredPlugin>();

        var natives = Natives();
        var all = slugs.All(natives.ContainsKey) ? natives : await AllAsync(ct);
        return slugs.Select(all.GetValueOrDefault).OfType<RegisteredPlugin>()
            .OrderBy(p => p.IsNative ? 0 : 1).ThenBy(p => p.Slug, StringComparer.Ordinal).ToList();
    }

    /// <summary>The plugin objects <c>Admin/Plugins/Index</c> lists. Never their config.</summary>
    public async Task<List<Dictionary<string, object?>>> ListingAsync(CancellationToken ct = default)
    {
        var rows = await _db.Plugins.AsNoTracking().ToDictionaryAsync(r => r.Slug, StringComparer.Ordinal, ct);
        return (await AllAsync(ct)).Values.OrderBy(p => p.Slug, StringComparer.Ordinal).Select(plugin =>
        {
            var row = rows.GetValueOrDefault(plugin.Slug);
            var manifest = plugin.Manifest;
            return new Dictionary<string, object?>
            {
                ["slug"] = plugin.Slug,
                ["name"] = manifest.DisplayName ?? plugin.Name,
                ["description"] = manifest.Description,
                ["version"] = manifest.Version,
                ["author"] = manifest.Author,
                ["author_url"] = manifest.Homepage,
                ["homepage"] = manifest.Homepage,
                ["is_active"] = row?.IsActive == true,
                ["activated_at"] = row?.IsActive == true ? PluginPayloads.Date(row.ActivatedAt) : null,
                ["installed_at"] = PluginPayloads.Date(row?.CreatedAt),
                ["source"] = plugin.Source,
                ["provides"] = new Dictionary<string, object?>
                {
                    ["menu_items"] = plugin.Pages.Count(p => p.Menu is not null),
                    ["widgets"] = manifest.Widgets.Count,
                    ["page_components"] = manifest.Components.Count,
                },
            };
        }).ToList();
    }

    // Lifecycle

    /// <summary>The row, then <c>onActivate</c>, then <c>plugin.activated</c>. False for an unknown slug. Idempotent.</summary>
    public async Task<bool> ActivateAsync(string slug, PluginActor actor, CancellationToken ct = default)
    {
        var plugin = await FindAsync(slug, ct);
        if (plugin is null) return false;

        var row = await _db.Plugins.FirstOrDefaultAsync(r => r.Slug == slug, ct);
        if (row is { IsActive: true }) return true;

        var now = DateTime.UtcNow;
        if (row is null)
        {
            row = new PluginRow { Slug = slug, CreatedAt = now };
            _db.Plugins.Add(row);
        }

        row.IsActive = true;
        row.ActivatedAt = now;
        row.DeactivatedAt = null;
        row.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);

        await LifecycleAsync(plugin, activate: true, actor, ct);
        await DispatchActionAsync("plugin.activated", Lifecycle(plugin), actor, ct);
        return true;
    }

    /// <summary><c>plugin.deactivated</c> while still active, <c>onDeactivate</c>, then the row. False for an unknown slug. Idempotent.</summary>
    public async Task<bool> DeactivateAsync(string slug, PluginActor actor, CancellationToken ct = default)
    {
        var plugin = await FindAsync(slug, ct);
        if (plugin is null) return false;

        var row = await _db.Plugins.FirstOrDefaultAsync(r => r.Slug == slug, ct);
        if (row is not { IsActive: true }) return true;

        await DispatchActionAsync("plugin.deactivated", Lifecycle(plugin), actor, ct);
        await LifecycleAsync(plugin, activate: false, actor, ct);

        row.IsActive = false;
        row.DeactivatedAt = DateTime.UtcNow;
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// <c>plugin.uninstalling</c>, deactivation, and the row goes. A registered plugin
    /// stays listed (its code is in the build) and keeps its stored data. A
    /// package-managed plugin is refused: the package manager removes it.
    /// </summary>
    public async Task<PluginUninstallResult> UninstallAsync(string slug, PluginActor actor, CancellationToken ct = default)
    {
        var plugin = await FindAsync(slug, ct);
        if (plugin is null) return PluginUninstallResult.Unknown;
        if (plugin.Source == PluginSources.Composer) return PluginUninstallResult.Refused;

        if (await _db.Plugins.AnyAsync(r => r.Slug == slug && r.IsActive, ct))
        {
            await DispatchActionAsync("plugin.uninstalling", Lifecycle(plugin), actor, ct);
        }

        await DeactivateAsync(slug, actor, ct);
        var row = await _db.Plugins.FirstOrDefaultAsync(r => r.Slug == slug, ct);
        if (row is not null)
        {
            _db.Plugins.Remove(row);
            await _db.SaveChangesAsync(ct);
        }

        return PluginUninstallResult.Uninstalled;
    }

    // Hooks

    /// <summary>Runs <paramref name="hook"/> on every active plugin that handles it, now. A plugin that throws is logged.</summary>
    public async Task DispatchActionAsync(string hook, JsonNode? payload, PluginActor actor, CancellationToken ct = default)
    {
        var sdk = new List<string>();
        var sdkHandles = false;
        foreach (var plugin in await ActivePluginsAsync(ct))
        {
            if (!plugin.IsNative)
            {
                sdk.Add(plugin.Name);
                sdkHandles |= plugin.HandlesAction(hook);
                continue;
            }

            if (!plugin.HandlesAction(hook)) continue;
            try
            {
                await plugin.Native!.HandleActionAsync(hook, payload?.DeepClone(), Context(plugin, actor), ct);
            }
            catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogError(e, "[Escalated plugins] {Plugin} failed on action {Hook}", plugin.Name, hook);
            }
        }

        if (sdkHandles)
        {
            await _bridge.ActionAsync(hook, payload, sdk, CtxFor(sdk, actor), ct);
        }
    }

    /// <summary>
    /// Passes <paramref name="value"/> through active plugins' filters for
    /// <paramref name="hook"/>, lowest priority first. A filter that throws is skipped
    /// and the value carries on. SDK filters run in the runtime in one call, at the
    /// place of the lowest-priority one.
    /// </summary>
    public async Task<JsonNode?> ApplyFilterAsync(string hook, JsonNode? value, PluginActor? actor = null, CancellationToken ct = default)
    {
        actor ??= PluginActor.None;
        var steps = new List<(int Priority, int Order, RegisteredPlugin? Plugin)>();
        var sdk = new List<string>();
        int? sdkPriority = null;
        foreach (var plugin in await ActivePluginsAsync(ct))
        {
            if (!plugin.IsNative) sdk.Add(plugin.Name);
            foreach (var filter in plugin.Manifest.FilterHooks.Where(f => f.Hook == hook))
            {
                if (plugin.IsNative) steps.Add((filter.Priority, steps.Count, plugin));
                else sdkPriority = Math.Min(sdkPriority ?? filter.Priority, filter.Priority);
            }
        }

        if (sdkPriority is not null) steps.Add((sdkPriority.Value, steps.Count, null));

        foreach (var (_, _, plugin) in steps.OrderBy(s => s.Priority).ThenBy(s => s.Order))
        {
            if (plugin is null)
            {
                value = await _bridge.FilterAsync(hook, value, sdk, CtxFor(sdk, actor), ct);
                continue;
            }

            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(PluginBridge.FilterTimeout);
                value = await plugin.Native!.ApplyFilterAsync(hook, value?.DeepClone(), Context(plugin, actor), timeout.Token)
                    .WaitAsync(timeout.Token);
            }
            catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogError(e, "[Escalated plugins] {Plugin} failed on filter {Hook}; skipped", plugin.Name, hook);
            }
        }

        return value;
    }

    /// <summary>
    /// <c>ctx.emit</c>: an action of the plugin's choosing to every active plugin,
    /// delivered in the background. The canonical events (and their legacy names) and
    /// the lifecycle actions belong to the host, so an emitted name that is one of
    /// them is dropped.
    /// </summary>
    public Task EmitAsync(string plugin, string hook, JsonNode? data, CancellationToken ct = default)
    {
        if (EventNames.Canonical.Contains(hook) || EventNames.Legacy.ContainsKey(hook) || LifecycleActions.Contains(hook))
        {
            _logger.LogWarning("[Escalated plugins] {Plugin} emitted \"{Hook}\", which belongs to the host; dropped.", plugin, hook);
            return Task.CompletedTask;
        }

        _relay.Enqueue(hook, data);
        return Task.CompletedTask;
    }

    /// <summary>Calls an endpoint (<paramref name="kind"/> <c>endpoint</c>) or webhook of an active plugin.</summary>
    /// <exception cref="PluginBridgeException">For an SDK plugin whose runtime failed or whose handler threw.</exception>
    public async Task<JsonNode?> CallHttpAsync(RegisteredPlugin plugin, string kind, PluginHttpRequest request, PluginActor actor,
        CancellationToken ct = default)
    {
        if (plugin.IsNative)
        {
            var ctx = Context(plugin, actor);
            var result = kind == "webhook"
                ? await plugin.Native!.HandleWebhookAsync(request, ctx, ct)
                : await plugin.Native!.HandleEndpointAsync(request, ctx, ct);
            return PluginJson.ToNode(result);
        }

        return await _bridge.HttpAsync(kind, plugin.Name, request, CtxFor(new[] { plugin.Name }, actor), ct);
    }

    // UI extensions

    /// <summary>
    /// <c>escalated.plugins</c> for the shared screens: active plugins' menu items,
    /// page components and dashboard widgets, without those whose capability the
    /// viewer lacks, and admin menu items for admins only. <c>{}</c> when no plugin
    /// is active.
    /// </summary>
    public async Task<JsonObject> UiExtensionsAsync(ClaimsPrincipal? user, CancellationToken ct = default)
    {
        var active = await ActivePluginsAsync(ct);
        if (active.Count == 0) return new JsonObject();

        var isAdmin = await _access.IsAdminAsync(user);
        var menuItems = new JsonArray();
        var pageComponents = new JsonObject();
        var dashboardWidgets = new JsonArray();

        foreach (var plugin in active)
        {
            foreach (var page in plugin.Pages)
            {
                if (page.Menu is not { } menu) continue;
                var section = menu.Section ?? "admin";
                if ((section == "admin" && !isAdmin) || !await _access.AllowsAsync(user, page.Capability, ct)) continue;
                menuItems.Add(new JsonObject
                {
                    ["label"] = menu.Label,
                    ["route"] = PageUrl(plugin, page.Route),
                    ["icon"] = menu.Icon,
                    ["position"] = menu.Position ?? 100,
                    ["section"] = section,
                    ["plugin"] = plugin.Slug,
                });
            }

            foreach (var component in plugin.Manifest.Components)
            {
                if (!await _access.AllowsAsync(user, component.Capability, ct)) continue;
                var key = $"{component.Page}.{component.Slot}";
                if (pageComponents[key] is not JsonArray slot) pageComponents[key] = slot = new JsonArray();
                slot.Add(new JsonObject
                {
                    ["plugin"] = plugin.Slug,
                    ["component"] = component.Component,
                    ["data"] = component.Props?.DeepClone() ?? new JsonObject(),
                    ["position"] = component.Order ?? 100,
                });
            }

            foreach (var widget in plugin.Manifest.Widgets)
            {
                if (!await _access.AllowsAsync(user, widget.Capability, ct)) continue;
                dashboardWidgets.Add(new JsonObject
                {
                    ["id"] = $"{plugin.Slug}.{widget.Component}",
                    ["title"] = widget.Label ?? widget.Component,
                    ["component"] = widget.Component,
                    ["plugin"] = plugin.Slug,
                    ["position"] = widget.Order ?? 100,
                    ["width"] = widget.Size ?? "half",
                });
            }
        }

        return new JsonObject
        {
            ["menuItems"] = menuItems,
            ["pageComponents"] = pageComponents,
            ["dashboardWidgets"] = dashboardWidgets,
        };
    }

    public Task<bool> AllowsAsync(ClaimsPrincipal? user, string? capability, CancellationToken ct = default) =>
        _access.AllowsAsync(user, capability, ct);

    public string PageUrl(RegisteredPlugin plugin, string route) =>
        $"/{_options.RoutePrefix.Trim('/')}/admin/plugins/{plugin.Slug}/{route.TrimStart('/')}".Replace("//", "/", StringComparison.Ordinal);

    /// <summary>The <c>ctx</c> a native plugin's handlers get.</summary>
    public PluginContext Context(RegisteredPlugin plugin, PluginActor actor) => new(_contexts, plugin.Name, actor);

    /// <summary>
    /// The ctx handler for a bridge call serving <paramref name="plugins"/>. With one
    /// plugin every call is that plugin's; otherwise the runtime must name one of
    /// them. A name outside the call's plugins is refused.
    /// </summary>
    private PluginCtxHandler CtxFor(IReadOnlyList<string> plugins, PluginActor actor) => (method, parameters, ct) =>
    {
        var named = PluginJson.String(parameters["plugin"]);
        string plugin;
        if (plugins.Count == 1) plugin = plugins[0];
        else if (named is not null && plugins.Contains(named)) plugin = named;
        else throw new InvalidOperationException($"{method} refused: the call is not serving plugin \"{named}\".");

        parameters.Remove("plugin");
        return _contexts.HandleAsync(plugin, actor, method, parameters, ct);
    };

    private async Task LifecycleAsync(RegisteredPlugin plugin, bool activate, PluginActor actor, CancellationToken ct)
    {
        try
        {
            if (plugin.IsNative)
            {
                if (activate) await plugin.Native!.OnActivateAsync(Context(plugin, actor), ct);
                else await plugin.Native!.OnDeactivateAsync(Context(plugin, actor), ct);
                return;
            }

            var ctx = CtxFor(new[] { plugin.Name }, actor);
            if (activate) await _bridge.ActivateAsync(plugin.Name, ctx, ct);
            else await _bridge.DeactivateAsync(plugin.Name, ctx, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(e, "[Escalated plugins] {Plugin} failed to {Transition}", plugin.Name, activate ? "activate" : "deactivate");
        }
    }

    private static JsonObject Lifecycle(RegisteredPlugin plugin) => new() { ["plugin"] = plugin.Name, ["slug"] = plugin.Slug };

    private async Task<List<string>> ActiveSlugsAsync(CancellationToken ct) =>
        await _db.Plugins.AsNoTracking().Where(r => r.IsActive).Select(r => r.Slug).ToListAsync(ct);

    private void Register(Dictionary<string, RegisteredPlugin> known, RegisteredPlugin plugin)
    {
        if (known.TryGetValue(plugin.Slug, out var other))
        {
            throw new InvalidOperationException($"Plugins \"{other.Name}\" and \"{plugin.Name}\" share the slug \"{plugin.Slug}\"; rename one.");
        }

        if (plugin.SkippedPages > 0)
        {
            _logger.LogWarning("[Escalated plugins] {Plugin} declares {Count} page(s) without a route or component; skipped.", plugin.Name, plugin.SkippedPages);
        }

        known[plugin.Slug] = plugin;
    }
}
