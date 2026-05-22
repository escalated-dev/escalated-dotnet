using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Escalated.Models;
using Escalated.Models.Newsletter;

namespace Escalated.Services.Newsletter;

/// <summary>
/// Renders a NewsletterDelivery to themed HTML.
///
/// Stage 1: Markdown -> canonical HTML (host integrators register a converter
///          via <see cref="NewsletterRendererOptions.MarkdownToHtml"/>; default is
///          escape+paragraph fallback).
/// Stage 2: Theme wrap via simple string interpolation on a shipped HTML template.
/// Stage 3: Optional click rewriting + tracking pixel injection.
/// </summary>
public class NewsletterRenderer
{
    private static readonly string[] AllowedSchemes = { "http", "https", "mailto", "tel" };
    private static readonly Regex AnchorRegex = new(@"(?i)(<a\s[^>]*\bhref=)(""|')(.*?)\2", RegexOptions.Compiled);
    private static readonly Regex MergeFieldRegex = new(@"\{\{\s*([a-zA-Z0-9_.]+)\s*\}\}", RegexOptions.Compiled);

    private readonly NewsletterRendererOptions _options;

    public NewsletterRenderer(NewsletterRendererOptions options)
    {
        _options = options;
    }

    public string Render(NewsletterDelivery delivery, Newsletter newsletter, Contact contact, NewsletterTemplate? template = null)
    {
        var bodyMd = newsletter.BodyMarkdown ?? template?.BodyMarkdown ?? string.Empty;
        var themeSlug = newsletter.Theme ?? template?.Theme ?? _options.DefaultTheme;

        var body = MarkdownToHtml(bodyMd);
        body = ResolveMergeFields(body, contact, delivery);

        var themed = RenderTheme(themeSlug, new Dictionary<string, string>
        {
            ["subject"] = newsletter.Subject ?? string.Empty,
            ["body"] = body,
            ["unsubscribe_url"] = UnsubscribeUrl(delivery),
            ["view_in_browser_url"] = ViewInBrowserUrl(delivery),
            ["brand.name"] = _options.Brand.Name,
            ["brand.accent"] = _options.Brand.Accent,
            ["brand.logo_url"] = _options.Brand.LogoUrl ?? string.Empty,
            ["brand.physical_address"] = _options.Brand.PhysicalAddress ?? string.Empty,
        });

        if (!_options.TrackingEnabled) return themed;
        return InjectPixel(RewriteLinks(themed, delivery), delivery);
    }

    public string UnsubscribeUrl(NewsletterDelivery d) =>
        $"{_options.BaseUrl.TrimEnd('/')}/escalated/n/u/{d.TrackingToken}";

    public string ViewInBrowserUrl(NewsletterDelivery d) =>
        $"{_options.BaseUrl.TrimEnd('/')}/escalated/n/v/{d.TrackingToken}";

    private string MarkdownToHtml(string md)
    {
        if (_options.MarkdownToHtml != null) return _options.MarkdownToHtml(md);
        var escaped = WebUtility.HtmlEncode(md);
        return "<p>" + string.Join("</p><p>", escaped.Split(new[] { "\n\n" }, StringSplitOptions.None)) + "</p>";
    }

    private string ResolveMergeFields(string html, Contact contact, NewsletterDelivery delivery) =>
        MergeFieldRegex.Replace(html, m =>
            WebUtility.HtmlEncode(ResolvePath(m.Groups[1].Value.Trim(), contact, delivery)));

    private string ResolvePath(string path, Contact contact, NewsletterDelivery delivery)
    {
        if (path == "contact.name") return contact.Name ?? string.Empty;
        if (path == "contact.first_name")
            return (contact.Name ?? string.Empty).Split(' ', 2).FirstOrDefault() ?? string.Empty;
        if (path == "contact.email") return contact.Email ?? string.Empty;
        if (path == "unsubscribe_url") return UnsubscribeUrl(delivery);
        if (path == "view_in_browser_url") return ViewInBrowserUrl(delivery);
        if (path.StartsWith("contact.metadata."))
        {
            var key = path["contact.metadata.".Length..];
            var meta = contact.GetMetadata();
            return meta.TryGetValue(key, out var v) ? v?.ToString() ?? string.Empty : string.Empty;
        }
        return string.Empty;
    }

    private string RenderTheme(string slug, Dictionary<string, string> ctx)
    {
        var dir = _options.ThemesDir;
        var path = Path.Combine(dir, $"{slug}.html");
        if (!File.Exists(path)) path = Path.Combine(dir, "default.html");
        var source = File.ReadAllText(path);

        // {{{ key }}} = raw, {{ key }} = HTML-encoded
        source = Regex.Replace(source, @"\{\{\{\s*([a-zA-Z0-9_.]+)\s*\}\}\}", m =>
            ctx.GetValueOrDefault(m.Groups[1].Value.Trim(), string.Empty));
        source = Regex.Replace(source, @"\{\{\s*([a-zA-Z0-9_.]+)\s*\}\}", m =>
            WebUtility.HtmlEncode(ctx.GetValueOrDefault(m.Groups[1].Value.Trim(), string.Empty)));

        return source;
    }

    private string RewriteLinks(string html, NewsletterDelivery delivery)
    {
        var unsub = UnsubscribeUrl(delivery);
        var view = ViewInBrowserUrl(delivery);
        return AnchorRegex.Replace(html, m =>
        {
            var prefix = m.Groups[1].Value;
            var quote = m.Groups[2].Value;
            var href = m.Groups[3].Value;
            if (string.IsNullOrEmpty(href) || href.StartsWith('#')) return m.Value;

            var scheme = (href.Split(':', 2).FirstOrDefault() ?? string.Empty).ToLowerInvariant();
            if (!AllowedSchemes.Contains(scheme)) return $"{prefix}{quote}#{quote}";
            if (scheme is "mailto" or "tel") return m.Value;
            if (href.StartsWith(unsub) || href.StartsWith(view)) return m.Value;

            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(href))
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');
            var tracked = $"{_options.BaseUrl.TrimEnd('/')}/escalated/n/c/{delivery.TrackingToken}?u={encoded}";
            return $"{prefix}{quote}{tracked}{quote}";
        });
    }

    private string InjectPixel(string html, NewsletterDelivery delivery)
    {
        var url = $"{_options.BaseUrl.TrimEnd('/')}/escalated/n/o/{delivery.TrackingToken}.gif";
        var pixel = $"<img src=\"{WebUtility.HtmlEncode(url)}\" width=\"1\" height=\"1\" alt=\"\" />";
        return html.Contains("</body>")
            ? html.Replace("</body>", pixel + "</body>")
            : html + pixel;
    }
}

public class NewsletterRendererOptions
{
    public string BaseUrl { get; set; } = "http://localhost";
    public string DefaultTheme { get; set; } = "default";
    public bool TrackingEnabled { get; set; } = true;
    public string ThemesDir { get; set; } = string.Empty;
    public Func<string, string>? MarkdownToHtml { get; set; }
    public NewsletterBrand Brand { get; set; } = new();
}

public class NewsletterBrand
{
    public string Name { get; set; } = "Support";
    public string Accent { get; set; } = "#2563eb";
    public string? LogoUrl { get; set; }
    public string? PhysicalAddress { get; set; }
}
