using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

namespace Escalated.Widget;

/// <summary>
/// Origin normalisation and matching for embedding the widget on other sites.
/// An entry is <c>scheme://host[:port]</c> (http or https, lowercased, default
/// port dropped, a trailing <c>/</c> allowed) or <c>scheme://*.host[:port]</c>,
/// which matches every subdomain of <c>host</c> at any depth but not
/// <c>host</c> itself. A bare <c>*</c> is never accepted.
/// </summary>
public static partial class WidgetOrigins
{
    /// <summary>
    /// <see cref="HttpContext.Items"/> key holding the allowed other-site origin
    /// of the current widget request.
    /// </summary>
    public const string ItemKey = "Escalated.Widget.CrossOrigin";

    /// <summary>The path every widget route lives under.</summary>
    public static readonly PathString WidgetPath = new("/support/widget");

    /// <summary>The widget script, served to every site with its own headers.</summary>
    public static readonly PathString ScriptPath = new("/support/widget/embed.js");

    [GeneratedRegex(@"^(https?)://(\*\.)?([^/?#*]+?)/?\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PatternRegex();

    [GeneratedRegex(@"[\s,]+")]
    private static partial Regex Separators();

    /// <summary>
    /// The request's <c>Origin</c> when it names another site, or null for
    /// same-origin requests and requests without an <c>Origin</c> header.
    /// </summary>
    public static string? CrossOrigin(HttpRequest request)
    {
        var header = request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(header) || header == "null")
        {
            return null;
        }

        var origin = Normalize(header);
        if (origin is null || origin == Normalize($"{request.Scheme}://{request.Host.Value}"))
        {
            return null;
        }

        return origin;
    }

    /// <summary>Whether any of <paramref name="entries"/> allows <paramref name="origin"/>.</summary>
    public static bool Allows(IEnumerable<string> entries, string origin) =>
        MatchesAny(Patterns(entries), origin);

    /// <summary>Whether any normalised pattern matches <paramref name="origin"/>.</summary>
    public static bool MatchesAny(IEnumerable<string> patterns, string origin)
    {
        var normalized = Normalize(origin);
        return normalized is not null && patterns.Any(pattern => Matches(pattern, normalized));
    }

    /// <summary>The valid entries, normalised and without duplicates, in order.</summary>
    public static IReadOnlyList<string> Patterns(IEnumerable<string?> entries)
    {
        var patterns = new List<string>();
        foreach (var entry in entries)
        {
            var pattern = NormalizePattern(entry ?? string.Empty);
            if (pattern is not null && !patterns.Contains(pattern))
            {
                patterns.Add(pattern);
            }
        }

        return patterns;
    }

    /// <summary>Splits a newline- or comma-separated list.</summary>
    public static IEnumerable<string> Split(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : Separators().Split(value).Where(entry => entry.Length > 0);

    /// <summary>
    /// An admin-entered origin or <c>*.</c> pattern, normalised, or null when it
    /// is not a bare http(s) origin.
    /// </summary>
    public static string? NormalizePattern(string entry)
    {
        var match = PatternRegex().Match(entry.Trim());
        if (!match.Success)
        {
            return null;
        }

        var host = Normalize(match.Groups[1].Value.ToLowerInvariant() + "://" + match.Groups[3].Value);
        if (host is null)
        {
            return null;
        }

        return match.Groups[2].Success ? host.Replace("://", "://*.", StringComparison.Ordinal) : host;
    }

    private static bool Matches(string pattern, string origin)
    {
        var wildcard = pattern.IndexOf("://*.", StringComparison.Ordinal);
        if (wildcard < 0)
        {
            return pattern == origin;
        }

        var scheme = pattern[..wildcard];
        var suffix = pattern[(wildcard + 5)..];
        if (!origin.StartsWith(scheme + "://", StringComparison.Ordinal))
        {
            return false;
        }

        var host = origin[(scheme.Length + 3)..];
        return host.EndsWith("." + suffix, StringComparison.Ordinal) && host.Length > suffix.Length + 1;
    }

    /// <summary>Anything after the authority but a single trailing <c>/</c>.</summary>
    private static bool HasPath(string origin)
    {
        var start = origin.IndexOf("://", StringComparison.Ordinal);
        var authority = start < 0 ? origin : origin[(start + 3)..];
        if (authority.EndsWith('/'))
        {
            authority = authority[..^1];
        }

        return authority.Contains('/') || authority.Contains('\\');
    }

    /// <summary><c>scheme://host[:port]</c>, lowercased, default ports dropped.</summary>
    private static string? Normalize(string origin)
    {
        origin = origin.Trim();
        if (origin.Any(char.IsWhiteSpace)
            || !Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(uri.Host)
            || uri.UserInfo.Length > 0
            || origin.Contains('@')
            || origin.Contains('?')
            || origin.Contains('#')
            || uri.AbsolutePath != "/"
            || HasPath(origin))
        {
            return null;
        }

        var host = uri.Host.ToLowerInvariant();
        return uri.IsDefaultPort
            ? $"{uri.Scheme}://{host}"
            : $"{uri.Scheme}://{host}:{uri.Port}";
    }
}
