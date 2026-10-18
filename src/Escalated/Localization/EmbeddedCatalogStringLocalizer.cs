using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Localization;

namespace Escalated.Localization;

internal sealed partial class EmbeddedCatalogStringLocalizer : IStringLocalizer
{
    private const string ResourcePrefix = "Escalated.Resources.locales.";
    private static readonly Assembly CatalogAssembly = typeof(EmbeddedCatalogStringLocalizer).Assembly;
    private static readonly FrozenDictionary<string, string> ResourceNames = CatalogAssembly.GetManifestResourceNames()
        .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal))
        .ToFrozenDictionary(name => name[ResourcePrefix.Length..^5], name => name, StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, Lazy<FrozenDictionary<string, string>>> Catalogs = new();

    internal static readonly EmbeddedCatalogStringLocalizer Instance = new();

    public LocalizedString this[string name]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(name);
            foreach (var culture in Cultures(includeParents: true))
            {
                if (Catalog(culture).TryGetValue(name, out var value))
                {
                    return new LocalizedString(name, value, resourceNotFound: false, ResourceNames[culture]);
                }
            }

            return new LocalizedString(name, name, resourceNotFound: true, ResourcePrefix);
        }
    }

    public LocalizedString this[string name, params object[] arguments]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(arguments);
            var value = this[name];
            var formatted = arguments.Length == 0 ? value.Value : Format(name, value.Value, arguments);
            return new LocalizedString(name, formatted, value.ResourceNotFound, value.SearchedLocation);
        }
    }

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var culture in Cultures(includeParentCultures))
        {
            foreach (var pair in Catalog(culture))
            {
                if (seen.Add(pair.Key))
                {
                    yield return new LocalizedString(pair.Key, pair.Value, resourceNotFound: false, ResourceNames[culture]);
                }
            }
        }
    }

    private static IEnumerable<string> Cultures(bool includeParents)
    {
        var culture = CultureInfo.CurrentUICulture;
        if (culture.Name.Length == 0)
        {
            yield return "en";
            yield break;
        }

        var includedEnglish = false;
        while (culture.Name.Length != 0)
        {
            yield return culture.Name;
            includedEnglish |= culture.Name.Equals("en", StringComparison.OrdinalIgnoreCase);
            if (!includeParents)
            {
                yield break;
            }
            culture = culture.Parent;
        }

        if (!includedEnglish)
        {
            yield return "en";
        }
    }

    private static FrozenDictionary<string, string> Catalog(string culture)
    {
        // Only the finite set of embedded resources enters the cache. Unknown
        // request cultures do not create cache entries or filesystem lookups.
        if (!ResourceNames.TryGetValue(culture, out var resource))
        {
            return FrozenDictionary<string, string>.Empty;
        }

        return Catalogs.GetOrAdd(resource, name => new Lazy<FrozenDictionary<string, string>>(() => Load(name))).Value;
    }

    private static FrozenDictionary<string, string> Load(string resource)
    {
        using var stream = CatalogAssembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Missing embedded locale resource: {resource}");
        using var document = JsonDocument.Parse(stream);
        var strings = new Dictionary<string, string>(StringComparer.Ordinal);
        Flatten(document.RootElement, string.Empty, strings);
        return strings.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static void Flatten(JsonElement node, string prefix, Dictionary<string, string> strings)
    {
        foreach (var property in node.EnumerateObject())
        {
            var key = prefix.Length == 0 ? property.Name : $"{prefix}.{property.Name}";
            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                Flatten(property.Value, key, strings);
            }
            else if (property.Value.ValueKind == JsonValueKind.String)
            {
                strings.Add(key, property.Value.GetString()!);
            }
        }
    }

    private static string Format(string name, string template, object[] arguments)
    {
        // Shared catalogs use {count}/{name}. A dictionary names those values
        // explicitly; positional calls follow their order in the English text
        // so a translation can reorder placeholders without reordering values.
        if (arguments.Length == 1 && arguments[0] is IReadOnlyDictionary<string, object?> parameters && NamedPlaceholder().IsMatch(template))
        {
            return NamedPlaceholder().Replace(template, match =>
            {
                if (!parameters.TryGetValue(match.Groups["name"].Value, out var value))
                {
                    return match.Value;
                }

                var format = match.Groups["format"].Value;
                return value is IFormattable formattable
                    ? formattable.ToString(format.Length == 0 ? null : format[1..], CultureInfo.CurrentCulture) ?? string.Empty
                    : Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty;
            });
        }

        var canonical = Catalog("en").TryGetValue(name, out var english) ? english : template;
        var positions = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Match match in NamedPlaceholder().Matches(canonical))
        {
            positions.TryAdd(match.Groups["name"].Value, positions.Count);
        }

        var composite = NamedPlaceholder().Replace(template, match =>
            positions.TryGetValue(match.Groups["name"].Value, out var position)
                ? "{" + position.ToString(CultureInfo.InvariantCulture) + match.Groups["format"].Value + "}"
                : match.Value);
        return string.Format(CultureInfo.CurrentCulture, composite, arguments);
    }

    [GeneratedRegex(@"(?<!\{)\{(?<name>[A-Za-z_][A-Za-z0-9_]*)(?<format>:[^{}]+)?\}(?!\})", RegexOptions.CultureInvariant)]
    private static partial Regex NamedPlaceholder();
}
