using Escalated.Authorization;
using Escalated.Configuration;
using Escalated.Plugins;
using Escalated.Ui;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Escalated.Controllers.Ui;

/// <summary>
/// Admin &gt; Plugins (<c>Escalated/Admin/Plugins/Index</c>) and the pages plugins
/// declare (<c>Escalated/Plugin/Page</c>), per the plugin contract.
///
/// <para>Plugins are installed by whoever deploys the host (container services and
/// npm packages), so there is no upload: <c>can_upload</c> is false and
/// <c>POST {prefix}/admin/plugins/upload</c> does not exist.</para>
/// </summary>
[Route("support/admin/plugins")]
[EscalatedPrefixedRoute]
[Authorize(Policy = EscalatedPolicies.Admin)]
public class AdminPluginsController : ControllerBase
{
    private readonly PluginRegistry _plugins;
    private readonly EscalatedOptions _options;
    private readonly ILogger<AdminPluginsController> _logger;

    public AdminPluginsController(PluginRegistry plugins, IOptions<EscalatedOptions> options, ILogger<AdminPluginsController> logger)
    {
        _plugins = plugins;
        _options = options.Value;
        _logger = logger;
    }

    private string IndexUrl => "/" + $"{_options.RoutePrefix.Trim('/')}/admin/plugins".TrimStart('/');

    private PluginActor Actor => new(this.CurrentUserId(), User);

    /// <summary><c>escalated.admin.plugins.index</c>.</summary>
    [HttpGet("")]
    [EscalatedPage]
    public async Task<IActionResult> Index(CancellationToken ct) =>
        UiResults.Render("Escalated/Admin/Plugins/Index", new Dictionary<string, object?>
        {
            ["plugins"] = await _plugins.ListingAsync(ct),
            ["can_upload"] = false,
        });

    /// <summary><c>escalated.admin.plugins.activate</c>.</summary>
    [HttpPost("{slug}/activate")]
    public async Task<IActionResult> Activate(string slug, CancellationToken ct) =>
        await _plugins.ActivateAsync(slug, Actor, ct) ? Done("success", "Plugin activated.") : Unknown();

    /// <summary><c>escalated.admin.plugins.deactivate</c>.</summary>
    [HttpPost("{slug}/deactivate")]
    public async Task<IActionResult> Deactivate(string slug, CancellationToken ct) =>
        await _plugins.DeactivateAsync(slug, Actor, ct) ? Done("success", "Plugin deactivated.") : Unknown();

    /// <summary>
    /// There is no upload on this host (<c>can_upload</c> is false). The page and
    /// uninstall routes would otherwise make this path a 405, which ASP.NET decides
    /// before route constraints run.
    /// </summary>
    [HttpPost("upload")]
    public IActionResult Upload() => NotFound(new { message = "Not found." });

    /// <summary><c>escalated.admin.plugins.destroy</c>.</summary>
    [HttpDelete("{slug}")]
    public async Task<IActionResult> Destroy(string slug, CancellationToken ct) =>
        await _plugins.UninstallAsync(slug, Actor, ct) switch
        {
            PluginUninstallResult.Unknown => Unknown(),
            PluginUninstallResult.Refused => Done("error", "This plugin is installed by a package manager; remove it there.", StatusCodes.Status409Conflict),
            _ => Done("success", "Plugin uninstalled."),
        };

    /// <summary>
    /// A page an active plugin declares, as <c>Escalated/Plugin/Page</c> with the
    /// props its own <c>GET /{component}</c> endpoint returns, or <c>{}</c>.
    /// </summary>
    [HttpGet("{slug}/{**route}")]
    [EscalatedPage]
    public async Task<IActionResult> Page(string slug, string route, CancellationToken ct)
    {
        var plugin = await _plugins.ActiveAsync(slug, ct);
        var page = plugin?.Page(route ?? string.Empty);
        if (plugin is null || page is null) return NotFound(new { message = "Plugin page not found." });
        if (!await _plugins.AllowsAsync(User, page.Capability, ct))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "This plugin page needs a capability you do not have." });
        }

        return UiResults.Render("Escalated/Plugin/Page", new Dictionary<string, object?>
        {
            ["plugin"] = plugin.Name,
            ["component"] = page.Component,
            ["layout"] = page.Layout,
            ["props"] = await PagePropsAsync(plugin, page.Component, ct),
        });
    }

    private async Task<System.Text.Json.Nodes.JsonObject> PagePropsAsync(RegisteredPlugin plugin, string component, CancellationToken ct)
    {
        var path = "/" + component;
        var declared = plugin.Endpoints.FirstOrDefault(e => e.Method == "GET" && e.Path == path);
        if (declared is null) return new System.Text.Json.Nodes.JsonObject();

        try
        {
            var request = await PluginHttp.ReadAsync(Request, declared, new Dictionary<string, string>(), ct);
            return PluginHttp.PageProps(await _plugins.CallHttpAsync(plugin, "endpoint", request, Actor, ct));
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError("[Escalated plugins] {Plugin} failed to give props for {Component}: {Error}", plugin.Name, component, e.Message);
            return new System.Text.Json.Nodes.JsonObject();
        }
    }

    private IActionResult Unknown() =>
        WantsJson() ? NotFound(new { message = "Plugin not found." }) : Done("error", "Plugin not found.");

    private IActionResult Done(string type, string message, int jsonStatus = StatusCodes.Status200OK)
    {
        if (WantsJson())
        {
            return StatusCode(type == "success" ? StatusCodes.Status200OK : jsonStatus, new { message });
        }

        return new UiRedirectResult(IndexUrl)
        {
            Success = type == "success" ? message : null,
            Error = type == "error" ? message : null,
        };
    }

    private bool WantsJson() =>
        !InertiaHeaders.IsInertia(Request)
        && Request.Headers.Accept.ToString().Contains("json", StringComparison.OrdinalIgnoreCase)
        && !Request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase);
}
