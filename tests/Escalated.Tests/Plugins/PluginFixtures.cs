using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Escalated.Plugins;

namespace Escalated.Tests.Plugins;

/// <summary>What the fixture plugins were called with, in order. One per test host.</summary>
public sealed class PluginRecorder
{
    private readonly ConcurrentQueue<(string Plugin, string Call, JsonNode? Data)> _calls = new();

    public void Record(string plugin, string call, JsonNode? data = null) => _calls.Enqueue((plugin, call, data?.DeepClone()));

    public IReadOnlyList<(string Plugin, string Call, JsonNode? Data)> Calls => _calls.ToList();

    public IReadOnlyList<string> CallsOf(string plugin) => _calls.Where(c => c.Plugin == plugin).Select(c => c.Call).ToList();

    public IReadOnlyList<JsonNode?> Actions(string plugin, string hook) =>
        _calls.Where(c => c.Plugin == plugin && c.Call == "action:" + hook).Select(c => c.Data).ToList();
}

/// <summary>
/// A native plugin exercising most of the manifest: a page with a menu item and
/// props, a capability-guarded page and endpoint, a <c>:id</c> endpoint, a throwing
/// endpoint, a webhook, a filter, a slot component and a widget.
/// </summary>
public sealed class CrmPlugin : EscalatedPlugin
{
    public const string Name = "@acme/escalated-plugin-crm";
    public const string Slug = "acme--escalated-plugin-crm";

    private readonly PluginRecorder _recorder;

    public CrmPlugin(PluginRecorder recorder)
    {
        _recorder = recorder;
    }

    public override PluginManifest Manifest { get; } = new()
    {
        Name = Name,
        DisplayName = "Acme CRM",
        Version = "2.1.0",
        Description = "Customer records next to tickets.",
        Author = "Acme",
        Homepage = "https://acme.test/crm",
        Pages = new[]
        {
            new PluginPage { Route = "/dashboard", Component = "CrmDashboard", Menu = new PluginMenu { Label = "CRM", Icon = "users", Position = 5 } },
            new PluginPage { Route = "secret", Component = "CrmSecret", Capability = "crm.view", Menu = new PluginMenu { Label = "Secret", Section = "agent" } },
            new PluginPage { Route = "plain", Component = "CrmPlain", Layout = "bare" },
            new PluginPage { Route = "", Component = "Nowhere" },
        },
        Components = new[]
        {
            new PluginSlotComponent { Page = "ticket.show", Slot = "sidebar", Component = "CrmCard", Props = new JsonObject { ["compact"] = true }, Order = 3 },
            new PluginSlotComponent { Page = "ticket.show", Slot = "sidebar", Component = "CrmSecretCard", Capability = "crm.view" },
        },
        Widgets = new[] { new PluginWidget { Component = "CrmStats", Label = "CRM stats", Size = "full", Order = 2 } },
        Endpoints = new[]
        {
            new PluginRoute("GET", "/CrmDashboard"),
            new PluginRoute("POST", "/contacts/:id"),
            new PluginRoute("GET", "/guarded", "crm.view"),
            new PluginRoute("GET", "/boom"),
            new PluginRoute("GET", "/ctx"),
        },
        Webhooks = new[] { new PluginRoute("POST", "/inbound") },
        FilterHooks = new[] { new PluginFilterHook("demo.value", 20) },
    };

    public override async Task OnActivateAsync(PluginContext ctx, CancellationToken ct)
    {
        _recorder.Record(Name, "onActivate");
        await ctx.ConfigSetAsync(new JsonObject { ["activated"] = true }, ct);
    }

    public override Task OnDeactivateAsync(PluginContext ctx, CancellationToken ct)
    {
        _recorder.Record(Name, "onDeactivate");
        return Task.CompletedTask;
    }

    public override async Task HandleActionAsync(string hook, JsonNode? payload, PluginContext ctx, CancellationToken ct)
    {
        _recorder.Record(Name, "action:" + hook, payload);
        if (hook == "ticket.created" && payload?["subject"]?.GetValue<string>() == "Write back")
        {
            await ctx.CallAsync("replies.create", new JsonObject
            {
                ["data"] = new JsonObject { ["ticket_id"] = payload["id"]!.DeepClone(), ["body"] = "Seen by CRM", ["is_internal"] = true },
            }, ct);
        }
    }

    public override Task<JsonNode?> ApplyFilterAsync(string hook, JsonNode? value, PluginContext ctx, CancellationToken ct) =>
        Task.FromResult<JsonNode?>(value?.GetValue<string>() + "|crm");

    public override async Task<object?> HandleEndpointAsync(PluginHttpRequest request, PluginContext ctx, CancellationToken ct)
    {
        _recorder.Record(Name, $"endpoint:{request.Method} {request.Path}");
        return request.Path switch
        {
            "/CrmDashboard" => new { open_deals = 4 },
            "/contacts/:id" => PluginHttpResponse.Json(new
            {
                path = request.Path,
                @params = request.Params,
                query = request.Query,
                body = request.Body,
                header_names = request.Headers.Keys.ToList(),
                user = await ctx.CurrentUserAsync(ct),
            }, 201, new Dictionary<string, string> { ["x-crm"] = "yes" }),
            "/guarded" => new { ok = true },
            "/ctx" => await ctx.ConfigAllAsync(ct),
            _ => throw new InvalidOperationException("kaboom secret"),
        };
    }

    public override Task<object?> HandleWebhookAsync(PluginHttpRequest request, PluginContext ctx, CancellationToken ct)
    {
        _recorder.Record(Name, $"webhook:{request.Method} {request.Path}", request.Body);
        return Task.FromResult<object?>(new { received = true, raw = request.RawBodyBase64 });
    }
}

/// <summary>A second native plugin, whose lifecycle callbacks throw and which lists its action hooks.</summary>
public sealed class AuditPlugin : EscalatedPlugin
{
    public const string Name = "acme-audit";
    public const string Slug = "acme-audit";

    private readonly PluginRecorder _recorder;

    public AuditPlugin(PluginRecorder recorder)
    {
        _recorder = recorder;
    }

    public override PluginManifest Manifest { get; } = new()
    {
        Name = Name,
        ActionHooks = new[] { "ticket.created", "reply.created", "plugin.activated", "plugin.deactivated", "plugin.uninstalling", "audit.ping" },
        FilterHooks = new[] { new PluginFilterHook("demo.value", 5), new PluginFilterHook("demo.throws", 1) },
    };

    public override Task OnActivateAsync(PluginContext ctx, CancellationToken ct)
    {
        _recorder.Record(Name, "onActivate");
        throw new InvalidOperationException("activation callback failed");
    }

    public override Task OnDeactivateAsync(PluginContext ctx, CancellationToken ct)
    {
        _recorder.Record(Name, "onDeactivate");
        throw new InvalidOperationException("deactivation callback failed");
    }

    public override Task HandleActionAsync(string hook, JsonNode? payload, PluginContext ctx, CancellationToken ct)
    {
        _recorder.Record(Name, "action:" + hook, payload);
        if (hook == "reply.created") throw new InvalidOperationException("action failed");
        return Task.CompletedTask;
    }

    public override Task<JsonNode?> ApplyFilterAsync(string hook, JsonNode? value, PluginContext ctx, CancellationToken ct) =>
        hook == "demo.throws"
            ? throw new InvalidOperationException("filter failed")
            : Task.FromResult<JsonNode?>(value?.GetValue<string>() + "|audit");
}
