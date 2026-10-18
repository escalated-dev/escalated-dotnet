using System.Reflection;
using Microsoft.Extensions.Localization;

namespace Escalated.Localization;

/// <summary>
/// An <see cref="IStringLocalizerFactory"/> that produces a
/// <see cref="ChainedStringLocalizer"/> wrapping:
///   1. A plugin-local override localizer rooted at
///      <c>Resources/Overrides/</c>.
///   2. The optional <c>Escalated.Locale.LocaleProvider</c> localizer.
///   3. The canonical JSON catalogs embedded in this package.
/// </summary>
/// <remarks>
/// Hosts that install <c>Escalated.Locale</c> can supply its static
/// <c>Escalated.Locale.LocaleProvider.CreateLocalizer(string baseName, string location)</c>
/// method returning an <see cref="IStringLocalizer"/> backed by the
/// embedded JSON catalog. The vendored catalogs remain available when that
/// optional package is absent or a key is missing from its localizer.
/// </remarks>
public sealed class EscalatedLocalizerFactory : IStringLocalizerFactory
{
    private readonly IStringLocalizerFactory _localFactory;

    public EscalatedLocalizerFactory(IStringLocalizerFactory localFactory)
    {
        _localFactory = localFactory;
    }

    public IStringLocalizer Create(Type resourceSource)
    {
        var local = _localFactory.Create(resourceSource);
        var central = TryCreateCentralLocalizer(
            resourceSource.Name,
            resourceSource.Namespace ?? string.Empty);
        return central is null
            ? new ChainedStringLocalizer(local, EmbeddedCatalogStringLocalizer.Instance)
            : new ChainedStringLocalizer(local, central, EmbeddedCatalogStringLocalizer.Instance);
    }

    public IStringLocalizer Create(string baseName, string location)
    {
        var local = _localFactory.Create(baseName, location);
        var central = TryCreateCentralLocalizer(baseName, location);
        return central is null
            ? new ChainedStringLocalizer(local, EmbeddedCatalogStringLocalizer.Instance)
            : new ChainedStringLocalizer(local, central, EmbeddedCatalogStringLocalizer.Instance);
    }

    /// <summary>
    /// Reflectively invoke
    /// <c>Escalated.Locale.LocaleProvider.CreateLocalizer</c>. Returns
    /// <c>null</c> if the package is not yet published or the method
    /// is not present; the embedded catalog then supplies the fallback.
    /// </summary>
    private static IStringLocalizer? TryCreateCentralLocalizer(string baseName, string location)
    {
        try
        {
            var providerType = Type.GetType(
                "Escalated.Locale.LocaleProvider, Escalated.Locale",
                throwOnError: false);
            if (providerType is null)
            {
                return null;
            }
            var method = providerType.GetMethod(
                "CreateLocalizer",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(string), typeof(string) },
                modifiers: null);
            if (method is null)
            {
                return null;
            }
            return method.Invoke(null, new object[] { baseName, location }) as IStringLocalizer;
        }
        catch
        {
            // Defensive: if the central package is mid-publish or its
            // surface changes, use the embedded catalog after local overrides.
            return null;
        }
    }
}
