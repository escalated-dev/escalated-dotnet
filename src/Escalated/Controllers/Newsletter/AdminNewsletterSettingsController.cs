using System.Globalization;
using System.Text.Json;
using Escalated.Configuration;
using Escalated.Data;
using Escalated.Models;
using Escalated.Services.Newsletter;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Escalated.Controllers.Newsletter;

[ApiController]
[NewsletterEnabled]
public class AdminNewsletterSettingsController : ControllerBase
{
    private static readonly Dictionary<string, string> SettingTypes = new()
    {
        ["default_from"] = "string",
        ["default_reply_to"] = "string",
        ["default_theme"] = "string",
        ["rate_limit_per_minute"] = "number",
        ["batch_size"] = "number",
        ["tracking_enabled"] = "boolean",
    };

    private readonly EscalatedDbContext _db;
    private readonly NewsletterPermissionService _permissions;
    private readonly IOptions<EscalatedOptions> _options;

    public AdminNewsletterSettingsController(
        EscalatedDbContext db,
        NewsletterPermissionService permissions,
        IOptions<EscalatedOptions> options)
    {
        _db = db;
        _permissions = permissions;
        _options = options;
    }

    [HttpGet]
    public async Task<IActionResult> Show(CancellationToken ct)
    {
        await _permissions.RequireAsync(HttpContext, "newsletters.manage", ct);
        var settings = new Dictionary<string, object?>();
        foreach (var key in SettingTypes.Keys)
        {
            var row = await _db.Settings.SingleOrDefaultAsync(s => s.Key == SettingKey(key), ct);
            settings[key] = row?.Value is not null
                ? ParseStoredValue(key, row.Value)
                : ConfigFallback(key);
        }

        return NewsletterHttp.Inertia(this, "Escalated/Admin/Newsletters/Settings", new
        {
            settings,
            themes = DiscoverThemes(),
        });
    }

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] JsonElement body, CancellationToken ct)
    {
        await _permissions.RequireAsync(HttpContext, "newsletters.manage", ct);
        var data = new Dictionary<string, object?>
        {
            ["default_from"] = NewsletterHttp.Email(NewsletterHttp.OptionalString(body, "default_from", 320), "default_from"),
            ["default_reply_to"] = NewsletterHttp.Email(NewsletterHttp.OptionalString(body, "default_reply_to", 320), "default_reply_to"),
            ["default_theme"] = NewsletterHttp.RequiredString(body, "default_theme", 64),
            ["rate_limit_per_minute"] = NewsletterHttp.RequiredInt(body, "rate_limit_per_minute", 1, 10000),
            ["batch_size"] = NewsletterHttp.RequiredInt(body, "batch_size", 1, 1000),
            ["tracking_enabled"] = NewsletterHttp.RequiredBool(body, "tracking_enabled"),
        };

        foreach (var (key, type) in SettingTypes)
        {
            var value = data[key];
            var stored = value is bool boolean
                ? (boolean ? "1" : "0")
                : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;

            var row = await _db.Settings.SingleOrDefaultAsync(s => s.Key == SettingKey(key), ct);
            if (row is null)
            {
                row = new EscalatedSettings
                {
                    Key = SettingKey(key),
                    CreatedAt = DateTime.UtcNow,
                };
                _db.Settings.Add(row);
            }

            row.Value = stored;
            row.Type = type;
            row.Group = "newsletter";
            row.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        return NewsletterHttp.Redirect(this, "/admin/newsletters/settings");
    }

    private static string SettingKey(string key) => $"newsletter.{key}";

    private object? ConfigFallback(string key)
    {
        var newsletter = _options.Value.Newsletters;
        return key switch
        {
            "default_from" => newsletter.DefaultFrom,
            "default_reply_to" => newsletter.DefaultReplyTo,
            "default_theme" => newsletter.DefaultTheme,
            "rate_limit_per_minute" => newsletter.RateLimitPerMinute,
            "batch_size" => newsletter.BatchSize,
            "tracking_enabled" => newsletter.TrackingEnabled,
            _ => null,
        };
    }

    private static object? ParseStoredValue(string key, string value) =>
        key switch
        {
            "rate_limit_per_minute" or "batch_size" => int.TryParse(value, out var number) ? number : null,
            "tracking_enabled" => value is "1" or "true",
            _ => value,
        };

    private string[] DiscoverThemes()
    {
        var dir = _options.Value.Newsletters.ThemesDir;
        if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
            return Directory.GetFiles(dir, "*.html").Select(f => Path.GetFileNameWithoutExtension(f)).ToArray();
        return ["default", "branded"];
    }
}
