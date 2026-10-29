using Escalated.Authorization;
using Escalated.Configuration;
using Escalated.Plugins;
using Escalated.Plugins.Bridge;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Escalated.Controllers;

/// <summary>
/// Plugin endpoints and webhooks (the plugin contract's "Endpoints and webhooks"):
/// <list type="bullet">
///   <item><c>{METHOD} {prefix}/api/plugins/{slug}{path}</c>: behind the admin guard
///   and the endpoint's capability. A state-changing call must come from a page
///   script (<c>X-Requested-With</c> or <c>X-Inertia</c>), which a cross-site form
///   cannot send with an admin's cookies.</item>
///   <item><c>{METHOD} {prefix}/webhooks/plugins/{slug}{path}</c>: no guard; the
///   plugin authenticates the sender.</item>
/// </list>
/// Only active plugins answer. The request is matched against the manifest's paths
/// (<c>:param</c> segments allowed) and dispatched on the manifest path.
/// </summary>
[Route("support")]
[EscalatedPrefixedRoute]
[Authorize(Policy = EscalatedPolicies.Admin)]
public class PluginHttpController : ControllerBase
{
    private readonly PluginRegistry _plugins;
    private readonly ILogger<PluginHttpController> _logger;

    public PluginHttpController(PluginRegistry plugins, ILogger<PluginHttpController> logger)
    {
        _plugins = plugins;
        _logger = logger;
    }

    [AcceptVerbs("GET", "HEAD", "POST", "PUT", "PATCH", "DELETE", "OPTIONS", Route = "api/plugins/{slug}/{**path}")]
    public async Task<IActionResult> Endpoint(string slug, string? path, CancellationToken ct)
    {
        var plugin = await _plugins.ActiveAsync(slug, ct);
        var match = plugin is null ? null : PluginHttp.Match(plugin.Endpoints, Request.Method.ToUpperInvariant(), path ?? string.Empty);
        if (plugin is null || match is null) return PluginHttp.Message(StatusCodes.Status404NotFound, "Not found.");

        var (route, parameters) = match.Value;
        if (!await _plugins.AllowsAsync(User, route.Capability, ct))
        {
            return PluginHttp.Message(StatusCodes.Status403Forbidden, "This endpoint needs a capability you do not have.");
        }

        if (!HttpMethods.IsGet(Request.Method) && !HttpMethods.IsHead(Request.Method) && !HttpMethods.IsOptions(Request.Method)
            && !Request.Headers.ContainsKey("X-Requested-With") && !Request.Headers.ContainsKey("X-Inertia"))
        {
            return PluginHttp.Message(StatusCodes.Status403Forbidden, "Send this request from a page script (X-Requested-With).");
        }

        return await CallAsync(plugin!, "endpoint", route, parameters, new PluginActor(this.CurrentUserId(), User), ct);
    }

    [AcceptVerbs("GET", "HEAD", "POST", "PUT", "PATCH", "DELETE", "OPTIONS", Route = "webhooks/plugins/{slug}/{**path}")]
    [AllowAnonymous]
    public async Task<IActionResult> Webhook(string slug, string? path, CancellationToken ct)
    {
        var plugin = await _plugins.ActiveAsync(slug, ct);
        var match = plugin is null ? null : PluginHttp.Match(plugin.Webhooks, Request.Method.ToUpperInvariant(), path ?? string.Empty);
        if (plugin is null || match is null) return PluginHttp.Message(StatusCodes.Status404NotFound, "Not found.");

        var (route, parameters) = match.Value;
        return await CallAsync(plugin!, "webhook", route, parameters, PluginActor.None, ct);
    }

    private async Task<IActionResult> CallAsync(RegisteredPlugin plugin, string kind, PluginRoute route,
        Dictionary<string, string> parameters, PluginActor actor, CancellationToken ct)
    {
        try
        {
            var request = await PluginHttp.ReadAsync(Request, route, parameters, ct);
            return PluginHttp.Response(await _plugins.CallHttpAsync(plugin, kind, request, actor, ct));
        }
        catch (PluginBridgeException e) when (!e.PluginError)
        {
            _logger.LogWarning("[Escalated plugins] {Plugin} {Kind} {Method} {Path}: {Error}", plugin.Name, kind, route.Method, route.Path, e.Message);
            return PluginHttp.Message(StatusCodes.Status503ServiceUnavailable, "The plugin runtime is not available.");
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(e, "[Escalated plugins] {Plugin} {Kind} {Method} {Path} threw", plugin.Name, kind, route.Method, route.Path);
            return PluginHttp.Message(StatusCodes.Status500InternalServerError, "The plugin could not handle this request.");
        }
    }
}
