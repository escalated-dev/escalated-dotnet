using Escalated.Ui;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Escalated.Plugins;

public static class EscalatedPluginServiceCollectionExtensions
{
    /// <summary>
    /// Registers a native plugin. It is listed in Admin &gt; Plugins, inactive until
    /// an admin activates it. Equivalent to registering <typeparamref name="TPlugin"/>
    /// as a scoped <see cref="IEscalatedPlugin"/>.
    /// </summary>
    public static IServiceCollection AddEscalatedPlugin<TPlugin>(this IServiceCollection services)
        where TPlugin : class, IEscalatedPlugin
    {
        services.AddScoped<IEscalatedPlugin, TPlugin>();
        return services;
    }
}

/// <summary>
/// Shares <c>escalated.plugins</c> on every page: the active plugins' menu items,
/// slot components and dashboard widgets (<see cref="PluginRegistry.UiExtensionsAsync"/>),
/// or <c>{}</c> with none active. Runs after <see cref="EscalatedUiSharedProps"/>,
/// which creates the <c>escalated</c> object.
/// </summary>
public sealed class PluginSharedProps : IEscalatedUiSharedProps
{
    private readonly PluginRegistry _plugins;

    public PluginSharedProps(PluginRegistry plugins)
    {
        _plugins = plugins;
    }

    public async Task ShareAsync(HttpContext context, IDictionary<string, object?> props, CancellationToken ct = default)
    {
        if (!props.TryGetValue("escalated", out var value) || value is not IDictionary<string, object?> escalated)
        {
            props["escalated"] = escalated = new Dictionary<string, object?>();
        }

        escalated["plugins"] = await _plugins.UiExtensionsAsync(context.User, ct);
    }
}

/// <summary>
/// Fails the host at startup when two native plugins share a slug, naming both,
/// rather than on the first request that lists them.
/// </summary>
internal sealed class PluginStartupCheck : IStartupFilter
{
    private readonly IServiceScopeFactory _scopes;

    public PluginStartupCheck(IServiceScopeFactory scopes)
    {
        _scopes = scopes;
    }

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        using (var scope = _scopes.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<PluginRegistry>().Natives();
        }

        return next;
    }
}
