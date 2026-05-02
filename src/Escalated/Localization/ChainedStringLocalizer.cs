using System.Globalization;
using Microsoft.Extensions.Localization;

namespace Escalated.Localization;

/// <summary>
/// An <see cref="IStringLocalizer"/> that consults a chain of inner
/// localizers in order and returns the first non-resource-not-found
/// match. Used to layer plugin-local override resources on top of the
/// central <c>Escalated.Locale</c> catalog.
/// </summary>
/// <remarks>
/// Order matters: the first localizer passed to the constructor is
/// consulted first. The standard wiring puts the plugin-local override
/// localizer first and the central <c>Escalated.Locale</c> localizer
/// second, so an override under <c>Resources/Overrides/</c> wins over
/// the centrally shipped string.
/// </remarks>
public sealed class ChainedStringLocalizer : IStringLocalizer
{
    private readonly IReadOnlyList<IStringLocalizer> _chain;

    public ChainedStringLocalizer(params IStringLocalizer[] chain)
    {
        if (chain is null || chain.Length == 0)
        {
            throw new ArgumentException("At least one localizer is required.", nameof(chain));
        }
        _chain = chain;
    }

    public LocalizedString this[string name]
    {
        get
        {
            foreach (var localizer in _chain)
            {
                var value = localizer[name];
                if (!value.ResourceNotFound)
                {
                    return value;
                }
            }
            // Fall through: return the not-found result from the last
            // localizer so callers see a consistent SearchedLocation.
            return _chain[^1][name];
        }
    }

    public LocalizedString this[string name, params object[] arguments]
    {
        get
        {
            foreach (var localizer in _chain)
            {
                var value = localizer[name, arguments];
                if (!value.ResourceNotFound)
                {
                    return value;
                }
            }
            return _chain[^1][name, arguments];
        }
    }

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
    {
        // Merge results from every localizer; later (lower priority)
        // entries do not overwrite earlier ones.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var localizer in _chain)
        {
            foreach (var entry in localizer.GetAllStrings(includeParentCultures))
            {
                if (seen.Add(entry.Name))
                {
                    yield return entry;
                }
            }
        }
    }
}
