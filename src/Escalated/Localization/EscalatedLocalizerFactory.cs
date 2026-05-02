using System.Reflection;
using Microsoft.Extensions.Localization;

namespace Escalated.Localization;

/// <summary>
/// An <see cref="IStringLocalizerFactory"/> that produces a
/// <see cref="ChainedStringLocalizer"/> wrapping:
///   1. A plugin-local override localizer rooted at
///      <c>Resources/Overrides/</c>.
///   2. The central <c>Escalated.Locale.LocaleProvider</c> localizer
///      (consumed via reflection so the plugin compiles even when the
///      central package is being upgraded).
/// </summary>
/// <remarks>
/// API assumption: the central NuGet package
/// <c>Escalated.Locale</c> exposes a static
/// <c>Escalated.Locale.LocaleProvider.CreateLocalizer(string baseName, string location)</c>
/// method returning an <see cref="IStringLocalizer"/> backed by the
/// embedded JSON catalog. This contract is owned by the
/// <c>escalated-locale</c> repo (Codex). If the published API differs,
/// only this factory needs to change.
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
            ? local
            : new ChainedStringLocalizer(local, central);
    }

    public IStringLocalizer Create(string baseName, string location)
    {
        var local = _localFactory.Create(baseName, location);
        var central = TryCreateCentralLocalizer(baseName, location);
        return central is null
            ? local
            : new ChainedStringLocalizer(local, central);
    }

    /// <summary>
    /// Reflectively invoke
    /// <c>Escalated.Locale.LocaleProvider.CreateLocalizer</c>. Returns
    /// <c>null</c> if the package is not yet published or the method
    /// is not present, which lets the plugin keep functioning against
    /// only its local resources during bootstrap.
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
            // surface changes, fall back to local-only resolution.
            return null;
        }
    }
}
