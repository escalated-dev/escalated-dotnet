using Escalated.Configuration;
using Escalated.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Escalated.Widget;

/// <summary>
/// The sites allowed to embed the widget: <see cref="WidgetOptions.AllowedOrigins"/>
/// (which includes <c>ESCALATED_WIDGET_ALLOWED_ORIGINS</c>) plus the
/// <c>widget_allowed_origins</c> admin setting, one origin per line or comma
/// separated.
/// </summary>
public sealed class WidgetOriginPolicy
{
    public const string SettingKey = "widget_allowed_origins";

    private readonly IOptions<EscalatedOptions> _options;
    private readonly SettingsService _settings;

    public WidgetOriginPolicy(IOptions<EscalatedOptions> options, SettingsService settings)
    {
        _options = options;
        _settings = settings;
    }

    /// <summary>Every valid configured and admin-entered pattern.</summary>
    public async Task<IReadOnlyList<string>> PatternsAsync(CancellationToken ct = default)
    {
        var setting = await _settings.GetAsync(SettingKey, null, ct);
        return WidgetOrigins.Patterns(_options.Value.Widget.AllowedOrigins.Concat(WidgetOrigins.Split(setting)));
    }

    public async Task<bool> AllowsAsync(string origin, CancellationToken ct = default)
    {
        // The configured list needs no database read.
        return AllowsConfigured(origin) || WidgetOrigins.MatchesAny(await PatternsAsync(ct), origin);
    }

    /// <summary>Whether the configured list alone allows the origin (no settings read).</summary>
    public bool AllowsConfigured(string origin) =>
        WidgetOrigins.Allows(_options.Value.Widget.AllowedOrigins, origin);
}

/// <summary>
/// Adds the comma-separated <c>ESCALATED_WIDGET_ALLOWED_ORIGINS</c> configuration
/// value (an environment variable under the default host configuration) to
/// <see cref="WidgetOptions.AllowedOrigins"/>.
/// </summary>
internal sealed class WidgetOriginsFromConfiguration : IPostConfigureOptions<EscalatedOptions>
{
    public const string Key = "ESCALATED_WIDGET_ALLOWED_ORIGINS";

    private readonly IConfiguration? _configuration;

    public WidgetOriginsFromConfiguration(IServiceProvider services)
    {
        _configuration = services.GetService(typeof(IConfiguration)) as IConfiguration;
    }

    public void PostConfigure(string? name, EscalatedOptions options)
    {
        var value = _configuration?[Key] ?? Environment.GetEnvironmentVariable(Key);
        foreach (var entry in WidgetOrigins.Split(value))
        {
            if (!options.Widget.AllowedOrigins.Contains(entry))
            {
                options.Widget.AllowedOrigins.Add(entry);
            }
        }
    }
}
