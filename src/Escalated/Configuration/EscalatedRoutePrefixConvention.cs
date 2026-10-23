using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace Escalated.Configuration;

/// <summary>
/// Marks a controller whose attribute routes start with <c>support/</c> as
/// following <see cref="EscalatedOptions.RoutePrefix"/>: the leading
/// <c>support</c> segment is replaced with the configured prefix when MVC builds
/// its routes. Unmarked controllers keep their routes as written.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class EscalatedPrefixedRouteAttribute : Attribute
{
}

/// <summary>Applies <see cref="EscalatedPrefixedRouteAttribute"/>. <c>AddEscalated()</c> registers it.</summary>
internal sealed class EscalatedRoutePrefixConvention : IControllerModelConvention
{
    internal const string DefaultPrefix = "support";

    private readonly string _prefix;

    public EscalatedRoutePrefixConvention(string prefix)
    {
        _prefix = prefix.Trim('/');
    }

    public void Apply(ControllerModel controller)
    {
        if (_prefix == DefaultPrefix
            || !controller.Attributes.OfType<EscalatedPrefixedRouteAttribute>().Any())
        {
            return;
        }

        foreach (var selector in controller.Selectors)
        {
            if (selector.AttributeRouteModel?.Template is { } template)
            {
                selector.AttributeRouteModel.Template = Reprefix(template);
            }
        }
    }

    private string Reprefix(string template)
    {
        if (template == DefaultPrefix) return _prefix;
        if (template.StartsWith(DefaultPrefix + "/", StringComparison.Ordinal))
        {
            var rest = template[(DefaultPrefix.Length + 1)..];
            return _prefix.Length == 0 ? rest : _prefix + "/" + rest;
        }

        return template;
    }
}
