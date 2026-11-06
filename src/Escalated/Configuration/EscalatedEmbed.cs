using Microsoft.AspNetCore.Http;

namespace Escalated.Configuration;

/// <summary>
/// Marks an endpoint of the admin while it is embedded in the host's admin panel (ADR
/// 2026-11-05-embeddable-admin). Only embedded admin endpoints carry it, and only while embedding
/// is on. <see cref="StandaloneTemplate"/> is the route template the action has in the standalone
/// admin (<c>support/admin/tags</c>), which Escalated's path-based checks (route permissions,
/// two-factor, feature switches) keep reading, so they apply exactly as they do there.
/// </summary>
public sealed class EscalatedEmbeddedAdminMetadata
{
    public EscalatedEmbeddedAdminMetadata(string standaloneTemplate)
    {
        StandaloneTemplate = standaloneTemplate;
    }

    public string StandaloneTemplate { get; }
}

/// <summary>The settings of <see cref="EmbedOptions"/>, normalised, and the helpers built on them.</summary>
public static class EscalatedEmbed
{
    /// <summary>Whether embedding is on.</summary>
    public static bool Enabled(EscalatedOptions options) => options.Ui.Embed.Enabled;

    /// <summary><c>layout</c> or <c>iframe</c>; anything else reads as <c>layout</c>.</summary>
    public static string Mode(EscalatedOptions options) =>
        string.Equals(options.Ui.Embed.Mode?.Trim(), EmbedOptions.IframeMode, StringComparison.OrdinalIgnoreCase)
            ? EmbedOptions.IframeMode
            : EmbedOptions.LayoutMode;

    /// <summary>The embed prefix without slashes (<c>admin/support</c> when blank).</summary>
    public static string Prefix(EscalatedOptions options)
    {
        var prefix = (options.Ui.Embed.Prefix ?? string.Empty).Trim().Trim('/');
        return prefix.Length == 0 ? EmbedOptions.DefaultPrefix : prefix;
    }

    /// <summary>
    /// The absolute path the admin screens live under: <c>/{RoutePrefix}/admin</c>, or
    /// <c>/{Embed:Prefix}</c> while embedding is on.
    /// </summary>
    public static string AdminBase(EscalatedOptions options) =>
        Enabled(options) ? "/" + Prefix(options) : StandaloneAdminBase(options);

    /// <summary><c>/{RoutePrefix}/admin</c>, where the standalone admin lives.</summary>
    public static string StandaloneAdminBase(EscalatedOptions options) =>
        ("/" + options.RoutePrefix.Trim('/') + "/admin").Replace("//", "/", StringComparison.Ordinal);

    /// <summary>The <c>layout</c> setting, or null when blank.</summary>
    public static string? Layout(EscalatedOptions options) =>
        string.IsNullOrWhiteSpace(options.Ui.Embed.Layout) ? null : options.Ui.Embed.Layout.Trim();

    /// <summary>The <c>root_view</c> setting, or null when blank.</summary>
    public static string? RootView(EscalatedOptions options) =>
        string.IsNullOrWhiteSpace(options.Ui.Embed.RootView) ? null : options.Ui.Embed.RootView.Trim();

    /// <summary>
    /// The origins allowed to frame the admin: exact <c>scheme://host[:port]</c> http(s) origins.
    /// <c>*</c>, wildcards, paths, queries, credentials and other schemes are dropped.
    /// </summary>
    public static IReadOnlyList<string> FrameAncestors(EscalatedOptions options)
    {
        var origins = new List<string>();
        foreach (var entry in options.Ui.Embed.FrameAncestors ?? new List<string>())
        {
            if (Origin(entry?.Trim()) is { } origin && !origins.Contains(origin))
            {
                origins.Add(origin);
            }
        }

        return origins;
    }

    /// <summary>The CSP directive for an embedded iframe response.</summary>
    public static string FrameAncestorsDirective(EscalatedOptions options) =>
        ("frame-ancestors 'self' " + string.Join(' ', FrameAncestors(options))).TrimEnd();

    /// <summary>Whether the request is served by an embedded admin endpoint.</summary>
    public static bool IsEmbeddedRequest(HttpContext? context) =>
        context?.GetEndpoint()?.Metadata.GetMetadata<EscalatedEmbeddedAdminMetadata>() is not null;

    /// <summary>
    /// For a route template of the standalone admin (<c>support/admin/tags/{id:int}</c>), what
    /// follows <c>admin</c> (<c>tags/{id:int}</c>, or empty for the admin root); null for any other
    /// template, and for two-factor enrolment, which is personal and does not move.
    /// </summary>
    internal static string? StandaloneAdminRest(string template, string routePrefix)
    {
        var path = template.Trim('/');
        foreach (var root in new[] { "support/admin", (routePrefix.Trim('/') + "/admin").TrimStart('/') }.Distinct())
        {
            string? rest = null;
            if (path.Equals(root, StringComparison.OrdinalIgnoreCase))
            {
                rest = string.Empty;
            }
            else if (path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))
            {
                rest = path[(root.Length + 1)..];
            }

            if (rest is null)
            {
                continue;
            }

            return rest.Equals("settings/two-factor", StringComparison.OrdinalIgnoreCase)
                || rest.StartsWith("settings/two-factor/", StringComparison.OrdinalIgnoreCase)
                ? null
                : rest;
        }

        return null;
    }

    private static string? Origin(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Contains('*')
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(uri.UserInfo)
            || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            return null;
        }

        var origin = uri.Scheme + "://" + uri.IdnHost.ToLowerInvariant();
        return uri.IsDefaultPort ? origin : origin + ":" + uri.Port;
    }
}
