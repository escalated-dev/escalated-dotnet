using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Escalated.Configuration;

/// <summary>
/// Embedding the admin in the host's own admin panel instead of the standalone admin at
/// <c>/{RoutePrefix}/admin</c> (configuration section <c>Escalated:Ui:Embed</c>; ADR
/// 2026-11-05-embeddable-admin). <b>Optional and off by default</b>: while
/// <see cref="Enabled"/> is false nothing here has any effect. See docs/embedding-admin.md.
/// </summary>
public class EmbedOptions
{
    public const string LayoutMode = "layout";
    public const string IframeMode = "iframe";
    public const string DefaultPrefix = "admin/support";

    /// <summary>Master switch (<c>ESCALATED_EMBED</c>). Default false.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// <c>layout</c> (a Vue + Inertia host layout, or a Razor root view, wraps the pages) or
    /// <c>iframe</c> (an allow-listed origin frames them). <c>ESCALATED_EMBED_MODE</c>. Default <c>layout</c>.
    /// </summary>
    public string Mode { get; set; } = LayoutMode;

    /// <summary>The URL prefix the admin moves under (<c>ESCALATED_EMBED_PREFIX</c>). Default <c>admin/support</c>.</summary>
    public string Prefix { get; set; } = DefaultPrefix;

    /// <summary>
    /// The host's "middleware" for the embedded admin routes: names of authorization policies
    /// every embedded admin request must also pass (for example a policy that requires your own
    /// admin role). Null or empty adds none; the admin routes keep Escalated's own policies and
    /// whatever authentication your app runs, as the standalone admin does.
    /// </summary>
    public List<string>? Policies { get; set; }

    /// <summary>
    /// MVC filters the host adds to every embedded admin action (code only, for example
    /// <c>new ServiceFilterAttribute(typeof(MyAdminFilter))</c>). They run after Escalated's own
    /// access filters.
    /// </summary>
    public List<IFilterMetadata> Filters { get; set; } = new();

    /// <summary>
    /// True drops Escalated's <c>escalated-admin</c> policy from the embedded admin routes, so
    /// <see cref="Policies"/> alone decide who reaches the admin (a signed-in user is still
    /// required). Two-factor and route permissions still apply. Only set it when your policies
    /// restrict the routes to admins. Default false.
    /// </summary>
    public bool ReplaceAdminGate { get; set; }

    /// <summary>Name of the host layout the frontend's <c>layouts</c> option should wrap admin pages in.</summary>
    public string? Layout { get; set; }

    /// <summary>
    /// A Razor view used as the root view of embedded admin pages (first visits only), for a
    /// server-rendered admin: its model is <see cref="Ui.EscalatedEmbedRootViewModel"/>. For
    /// example <c>~/Views/Shared/SupportAdmin.cshtml</c>. Null keeps the normal root view.
    /// </summary>
    public string? RootView { get; set; }

    /// <summary>
    /// iframe mode: the exact origins allowed to frame the admin (<c>https://admin.example.com</c>),
    /// <c>ESCALATED_EMBED_FRAME_ANCESTORS</c> comma-separated. <c>*</c>, wildcards, paths and
    /// anything other than http(s) are ignored. Default empty (only the app itself).
    /// </summary>
    public List<string> FrameAncestors { get; set; } = new();
}

public static class EscalatedEmbedOptionsExtensions
{
    /// <summary>
    /// Embeds Escalated's admin in your own admin panel (ADR 2026-11-05-embeddable-admin): the
    /// same as setting <c>Escalated:Ui:Embed</c>, with <c>Enabled</c> on.
    /// <code>
    /// builder.Services.Configure&lt;EscalatedOptions&gt;(o => o.EmbedAdmin(
    ///     prefix: "admin/support",
    ///     policies: new[] { "BackOfficeAdmin" },
    ///     layout: "Layouts/AdminLayout"));
    /// </code>
    /// </summary>
    public static EscalatedOptions EmbedAdmin(
        this EscalatedOptions options,
        string prefix = EmbedOptions.DefaultPrefix,
        IEnumerable<string>? policies = null,
        string? layout = null,
        string? rootView = null,
        string mode = EmbedOptions.LayoutMode,
        bool replaceAdminGate = false,
        IEnumerable<string>? frameAncestors = null,
        IEnumerable<IFilterMetadata>? filters = null)
    {
        if (mode is not (EmbedOptions.LayoutMode or EmbedOptions.IframeMode))
        {
            throw new ArgumentException($"EmbedAdmin mode must be 'layout' or 'iframe', got '{mode}'.", nameof(mode));
        }

        var embed = options.Ui.Embed;
        embed.Enabled = true;
        embed.Mode = mode;
        embed.Prefix = prefix;
        embed.Policies = policies?.ToList();
        embed.Layout = layout;
        embed.RootView = rootView;
        embed.ReplaceAdminGate = replaceAdminGate;
        embed.FrameAncestors = frameAncestors?.ToList() ?? new List<string>();
        embed.Filters = filters?.ToList() ?? new List<IFilterMetadata>();
        return options;
    }
}

/// <summary>
/// <c>ESCALATED_EMBED</c>, <c>ESCALATED_EMBED_MODE</c>, <c>ESCALATED_EMBED_PREFIX</c> and
/// <c>ESCALATED_EMBED_FRAME_ANCESTORS</c> (environment variables under the default host
/// configuration). They fill settings left at their defaults; explicit configuration or
/// <see cref="EscalatedEmbedOptionsExtensions.EmbedAdmin"/> wins.
/// </summary>
internal sealed class EmbedFromConfiguration : IPostConfigureOptions<EscalatedOptions>
{
    public const string EnabledKey = "ESCALATED_EMBED";
    public const string ModeKey = "ESCALATED_EMBED_MODE";
    public const string PrefixKey = "ESCALATED_EMBED_PREFIX";
    public const string FrameAncestorsKey = "ESCALATED_EMBED_FRAME_ANCESTORS";

    private readonly IConfiguration? _configuration;

    public EmbedFromConfiguration(IServiceProvider services)
    {
        _configuration = services.GetService(typeof(IConfiguration)) as IConfiguration;
    }

    public void PostConfigure(string? name, EscalatedOptions options)
    {
        var embed = options.Ui.Embed;

        if (!embed.Enabled && bool.TryParse(Read(EnabledKey), out var enabled))
        {
            embed.Enabled = enabled;
        }

        if (embed.Mode == EmbedOptions.LayoutMode && Read(ModeKey) is { } mode)
        {
            embed.Mode = mode.Trim();
        }

        if (embed.Prefix == EmbedOptions.DefaultPrefix && Read(PrefixKey) is { } prefix)
        {
            embed.Prefix = prefix.Trim();
        }

        if (embed.FrameAncestors.Count == 0 && Read(FrameAncestorsKey) is { } ancestors)
        {
            embed.FrameAncestors = ancestors.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }
    }

    private string? Read(string key)
    {
        var value = _configuration?[key] ?? Environment.GetEnvironmentVariable(key);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
