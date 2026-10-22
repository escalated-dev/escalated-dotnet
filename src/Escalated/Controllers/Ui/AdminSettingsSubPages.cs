using System.Net.Mail;
using System.Text.Json;
using Escalated.Authorization;
using Escalated.Configuration;
using Escalated.Data;
using Escalated.Models;
using Escalated.Services;
using Escalated.Ui;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Escalated.Controllers.Ui;

/// <summary>
/// The admin Settings sub-pages <c>Escalated/Admin/Settings/CsatSettings</c>,
/// <c>DataRetention</c>, <c>DatabaseConnection</c>, <c>EmailSettings</c> and
/// <c>PublicTickets</c>, with escalated-laravel's routes, props, validation and
/// flash messages (<c>CsatSettingsController</c>, <c>DataRetentionController</c>,
/// <c>DatabaseConnectionController</c>, <c>EmailSettingsController</c>,
/// <c>PublicTicketsSettingsController</c>). Every value lives in the settings
/// table under Laravel's keys. <c>GET</c>/<c>PUT settings/public-tickets</c> are
/// also the JSON admin API, so those answer only page requests.
/// </summary>
[Route("support/admin/settings")]
[Authorize(Policy = EscalatedPolicies.Admin)]
[EscalatedPage]
public class AdminSettingsSubPagesController : ControllerBase
{
    private const string DefaultCsatQuestion = "How would you rate your support experience?";

    /// <summary>The one connection this package has: the one the host gave <see cref="EscalatedDbContext"/>.</summary>
    public const string ConnectionName = "default";

    private static readonly string[] CsatScales = { "1-3", "1-5" };
    private static readonly string[] CsatTriggers = { "on_resolve", "delayed", "manual" };
    private static readonly string[] RetentionPeriods = { "never", "1_year", "2_years", "3_years", "5_years" };
    private static readonly string[] AuditRetentionPeriods = { "never", "90_days", "180_days", "365_days" };
    private static readonly string[] DkimStatuses = { "verified", "pending", "failed", "unknown" };
    private static readonly string[] GuestPolicyModes = { "unassigned", "guest_user", "prompt_signup" };

    private readonly EscalatedDbContext _db;
    private readonly SettingsService _settings;
    private readonly DataRetentionService _retention;
    private readonly EscalatedOptions _options;

    public AdminSettingsSubPagesController(EscalatedDbContext db, SettingsService settings, DataRetentionService retention,
        IOptions<EscalatedOptions> options)
    {
        _db = db;
        _settings = settings;
        _retention = retention;
        _options = options.Value;
    }

    private string Page(string path) => "/" + _options.RoutePrefix.Trim('/') + "/admin/settings/" + path;

    private async Task<string> SettingAsync(string key, string fallback, CancellationToken ct)
    {
        var value = await _settings.GetAsync(key, null, ct);
        return string.IsNullOrEmpty(value) ? fallback : value;
    }

    private async Task SaveAsync(IEnumerable<KeyValuePair<string, string?>> values, CancellationToken ct)
    {
        foreach (var (key, value) in values)
        {
            await _settings.SetAsync(key, value, ct);
        }
    }

    // --- CSAT ---

    [HttpGet("csat")]
    public async Task<IActionResult> Csat(CancellationToken ct)
    {
        _ = int.TryParse(await SettingAsync("csat_delay_hours", "0", ct), out var delay);
        return UiResults.Render("Escalated/Admin/Settings/CsatSettings", new
        {
            settings = new Dictionary<string, object?>
            {
                ["csat_question_text"] = await SettingAsync("csat_question_text", DefaultCsatQuestion, ct),
                ["csat_scale"] = await SettingAsync("csat_scale", "1-5", ct),
                ["csat_delivery_trigger"] = await SettingAsync("csat_delivery_trigger", "on_resolve", ct),
                ["csat_delay_hours"] = delay,
            },
        });
    }

    [HttpPost("csat")]
    public async Task<IActionResult> UpdateCsat(CancellationToken ct)
    {
        var input = await UiInput.ReadAsync(Request, ct);
        var errors = new Dictionary<string, string>();

        var question = input.String("csat_question_text");
        if (question is null) errors["csat_question_text"] = "The csat question text field is required.";
        else if (question.Length > 500) errors["csat_question_text"] = "The csat question text field must not be greater than 500 characters.";

        var scale = input.String("csat_scale");
        if (!CsatScales.Contains(scale)) errors["csat_scale"] = "The selected csat scale is invalid.";

        var trigger = input.String("csat_delivery_trigger");
        if (!CsatTriggers.Contains(trigger)) errors["csat_delivery_trigger"] = "The selected csat delivery trigger is invalid.";

        var rawDelay = input.String("csat_delay_hours");
        if (rawDelay is null) errors["csat_delay_hours"] = "The csat delay hours field is required.";
        else if (!int.TryParse(rawDelay, out var hours)) errors["csat_delay_hours"] = "The csat delay hours field must be an integer.";
        else if (hours is < 0 or > 168) errors["csat_delay_hours"] = "The csat delay hours field must be between 0 and 168.";

        var back = Page("csat");
        if (errors.Count > 0) return UiResults.Back(HttpContext, back, errors: errors);

        await SaveAsync(new Dictionary<string, string?>
        {
            ["csat_question_text"] = question,
            ["csat_scale"] = scale,
            ["csat_delivery_trigger"] = trigger,
            ["csat_delay_hours"] = int.Parse(rawDelay!).ToString(System.Globalization.CultureInfo.InvariantCulture),
        }, ct);

        return UiResults.Back(HttpContext, back, success: "CSAT settings updated.");
    }

    // --- Data retention ---

    [HttpGet("data-retention")]
    public async Task<IActionResult> DataRetention(CancellationToken ct)
    {
        var gdpr = await _settings.GetAsync("retention_user_data_gdpr", "0", ct);
        var preview = await _retention.PreviewAsync(ct: ct);
        return UiResults.Render("Escalated/Admin/Settings/DataRetention", new
        {
            settings = new Dictionary<string, object?>
            {
                ["retention_closed_tickets"] = await SettingAsync("retention_closed_tickets", "never", ct),
                ["retention_attachments"] = await SettingAsync("retention_attachments", "never", ct),
                ["retention_audit_logs"] = await SettingAsync("retention_audit_logs", "never", ct),
                ["retention_user_data_gdpr"] = gdpr is "1" or "true",
            },
            purgePreview = new Dictionary<string, object?>
            {
                ["tickets"] = preview.Tickets,
                ["attachments"] = preview.Attachments,
                ["audit_logs"] = preview.AuditLogs,
            },
        });
    }

    [HttpPost("data-retention")]
    public async Task<IActionResult> UpdateDataRetention(CancellationToken ct)
    {
        var input = await UiInput.ReadAsync(Request, ct);
        var errors = new Dictionary<string, string>();
        var values = new Dictionary<string, string?>();

        foreach (var (key, allowed) in new[]
                 {
                     ("retention_closed_tickets", RetentionPeriods),
                     ("retention_attachments", RetentionPeriods),
                     ("retention_audit_logs", AuditRetentionPeriods),
                 })
        {
            var value = input.String(key);
            var label = key.Replace('_', ' ');
            if (value is null) errors[key] = $"The {label} field is required.";
            else if (!allowed.Contains(value)) errors[key] = $"The selected {label} is invalid.";
            else values[key] = value;
        }

        switch (input.String("retention_user_data_gdpr")?.ToLowerInvariant())
        {
            case null:
                errors["retention_user_data_gdpr"] = "The retention user data gdpr field is required.";
                break;
            case "1" or "true":
                values["retention_user_data_gdpr"] = "1";
                break;
            case "0" or "false":
                values["retention_user_data_gdpr"] = "0";
                break;
            default:
                errors["retention_user_data_gdpr"] = "The retention user data gdpr field must be true or false.";
                break;
        }

        var back = Page("data-retention");
        if (errors.Count > 0) return UiResults.Back(HttpContext, back, errors: errors);

        await SaveAsync(values, ct);
        return UiResults.Back(HttpContext, back, success: "Data retention settings updated.");
    }

    // --- Database connection (read-only) ---

    /// <summary>
    /// Laravel's <c>ConnectionInspector</c> description of the connection Escalated's
    /// tables live on. A failed probe is reported, never thrown.
    /// </summary>
    private async Task<Dictionary<string, object?>> DescribeConnectionAsync(CancellationToken ct)
    {
        var description = new Dictionary<string, object?>
        {
            ["name"] = ConnectionName,
            ["driver"] = Driver(_db.Database.ProviderName),
            ["database"] = null,
            ["is_current"] = true,
            ["is_host_default"] = true,
            ["reachable"] = false,
            ["migrated"] = false,
            ["ticket_count"] = null,
            ["error"] = null,
        };

        try
        {
            var connection = _db.Database.GetDbConnection();
            description["database"] = string.IsNullOrEmpty(connection.DataSource) || connection.DataSource == connection.Database
                ? connection.Database
                : string.IsNullOrEmpty(connection.Database) || connection.Database == "main"
                    ? connection.DataSource
                    : $"{connection.Database} @ {connection.DataSource}";
        }
        catch (Exception)
        {
            // The label is cosmetic.
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            if (!await _db.Database.CanConnectAsync(timeout.Token))
            {
                description["error"] = "Could not connect.";
                return description;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            description["error"] = ex.Message;
            return description;
        }

        description["reachable"] = true;
        try
        {
            description["ticket_count"] = await _db.Tickets.IgnoreQueryFilters().CountAsync(timeout.Token);
            description["migrated"] = true;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // No tickets table: reachable, not migrated (Laravel reports no error for that).
            description["ticket_count"] = null;
        }

        return description;
    }

    private static string? Driver(string? provider) => provider switch
    {
        null => null,
        var p when p.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) => "sqlite",
        var p when p.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) => "pgsql",
        var p when p.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) => "sqlsrv",
        var p when p.Contains("MySql", StringComparison.OrdinalIgnoreCase) => "mysql",
        var p => p,
    };

    private string TablePrefix()
    {
        var table = _db.Model.FindEntityType(typeof(Ticket))?.GetTableName() ?? "escalated_tickets";
        return table.EndsWith("tickets", StringComparison.Ordinal) ? table[..^"tickets".Length] : string.Empty;
    }

    [HttpGet("database")]
    public async Task<IActionResult> Database(CancellationToken ct) =>
        UiResults.Render("Escalated/Admin/Settings/DatabaseConnection", new
        {
            connections = new[] { await DescribeConnectionAsync(ct) },
            current = ConnectionName,
            hostDefault = ConnectionName,
            pinnedByConfig = true,
            tablePrefix = TablePrefix(),
        });

    /// <summary>
    /// The connection is whatever the host registered <see cref="EscalatedDbContext"/>
    /// with, so it is never switched from here (Laravel's pinned-by-config answer).
    /// </summary>
    [HttpPost("database")]
    public IActionResult UpdateDatabase() =>
        UiResults.Back(HttpContext, Page("database"),
            error: "The database connection is set in configuration (the host's EscalatedDbContext registration) and cannot be changed from here.");

    [HttpPost("database/test")]
    public async Task<IActionResult> TestDatabase(CancellationToken ct)
    {
        var input = await UiInput.ReadAsync(Request, ct);
        var name = input.String("connection");
        string? error = name switch
        {
            null => "The connection field is required.",
            { Length: > 64 } => "The connection field must not be greater than 64 characters.",
            ConnectionName => null,
            _ => $"[{name}] is not a configured connection.",
        };

        var page = EscalatedPageAttribute.IsPageRequest(Request);
        if (error is not null)
        {
            var errors = new Dictionary<string, string> { ["connection"] = error };
            return page
                ? UiResults.Back(HttpContext, Page("database"), errors: errors)
                : UnprocessableEntity(new { message = error, errors = new { connection = new[] { error } } });
        }

        var description = await DescribeConnectionAsync(ct);
        if (!page) return Ok(description);

        return description["reachable"] is true
            ? UiResults.Back(HttpContext, Page("database"), success: $"Connected to [{ConnectionName}].")
            : UiResults.Back(HttpContext, Page("database"), error: $"Could not connect to [{ConnectionName}]: {description["error"]}");
    }

    // --- Email addresses ---

    private static Dictionary<string, object?> NormaliseAddress(JsonElement entry)
    {
        string? Text(string key) =>
            entry.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } s ? s : null;

        int? department = null;
        if (entry.TryGetProperty("department_id", out var raw))
        {
            if (raw.ValueKind == JsonValueKind.Number && raw.TryGetInt32(out var number)) department = number;
            else if (raw.ValueKind == JsonValueKind.String && int.TryParse(raw.GetString(), out var parsed)) department = parsed;
        }

        return new Dictionary<string, object?>
        {
            ["email"] = Text("email") ?? string.Empty,
            ["display_name"] = Text("display_name"),
            ["department_id"] = department,
            ["dkim_status"] = Text("dkim_status") ?? "unknown",
        };
    }

    private async Task<List<Dictionary<string, object?>>> AddressesAsync(CancellationToken ct)
    {
        var raw = await _settings.GetAsync("email_addresses", null, ct);
        if (string.IsNullOrEmpty(raw)) return new List<Dictionary<string, object?>>();
        try
        {
            using var document = JsonDocument.Parse(raw);
            return document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object).Select(NormaliseAddress).ToList()
                : new List<Dictionary<string, object?>>();
        }
        catch (JsonException)
        {
            return new List<Dictionary<string, object?>>();
        }
    }

    private static bool IsEmail(string value) =>
        MailAddress.TryCreate(value, out var address) && address.Address == value && value.Contains('@');

    [HttpGet("email")]
    public async Task<IActionResult> Email(CancellationToken ct) =>
        UiResults.Render("Escalated/Admin/Settings/EmailSettings", new
        {
            addresses = await AddressesAsync(ct),
            defaultReplyAddress = await _settings.GetAsync("default_reply_address", string.Empty, ct) ?? string.Empty,
        });

    /// <summary>As in Laravel, the addresses are stored only: outbound mail does not read them.</summary>
    [HttpPost("email")]
    public async Task<IActionResult> UpdateEmail(CancellationToken ct)
    {
        var input = await UiInput.ReadAsync(Request, ct);
        var errors = new Dictionary<string, string>();
        var addresses = new List<Dictionary<string, object?>>();

        var list = input.Json("addresses");
        if (list is null || list.Value.ValueKind == JsonValueKind.Null)
        {
            errors["addresses"] = "The addresses field is required.";
        }
        else if (list.Value.ValueKind != JsonValueKind.Array)
        {
            errors["addresses"] = "The addresses field must be an array.";
        }
        else
        {
            var index = 0;
            foreach (var entry in list.Value.EnumerateArray())
            {
                var key = $"addresses.{index++}.";
                if (entry.ValueKind != JsonValueKind.Object)
                {
                    errors[key + "email"] = "The email field is required.";
                    continue;
                }

                var email = entry.TryGetProperty("email", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
                if (string.IsNullOrEmpty(email)) errors[key + "email"] = "The email field is required.";
                else if (email.Length > 255) errors[key + "email"] = "The email field must not be greater than 255 characters.";
                else if (!IsEmail(email)) errors[key + "email"] = "The email field must be a valid email address.";

                if (entry.TryGetProperty("display_name", out var displayName) && displayName.ValueKind != JsonValueKind.Null)
                {
                    if (displayName.ValueKind != JsonValueKind.String) errors[key + "display_name"] = "The display name field must be a string.";
                    else if (displayName.GetString()!.Length > 255) errors[key + "display_name"] = "The display name field must not be greater than 255 characters.";
                }

                if (entry.TryGetProperty("department_id", out var department) && department.ValueKind != JsonValueKind.Null
                    && !(department.ValueKind == JsonValueKind.Number && department.TryGetInt32(out _))
                    && !(department.ValueKind == JsonValueKind.String && (department.GetString() == "" || int.TryParse(department.GetString(), out _))))
                {
                    errors[key + "department_id"] = "The department id field must be an integer.";
                }

                if (entry.TryGetProperty("dkim_status", out var dkim) && dkim.ValueKind != JsonValueKind.Null
                    && !(dkim.ValueKind == JsonValueKind.String && DkimStatuses.Contains(dkim.GetString())))
                {
                    errors[key + "dkim_status"] = "The selected dkim status is invalid.";
                }

                addresses.Add(NormaliseAddress(entry));
            }
        }

        var reply = input.String("default_reply_address");
        if (reply is not null)
        {
            if (reply.Length > 255) errors["default_reply_address"] = "The default reply address field must not be greater than 255 characters.";
            else if (!IsEmail(reply)) errors["default_reply_address"] = "The default reply address field must be a valid email address.";
        }

        var back = Page("email");
        if (errors.Count > 0) return UiResults.Back(HttpContext, back, errors: errors);

        var values = new Dictionary<string, string?> { ["email_addresses"] = JsonSerializer.Serialize(addresses) };
        if (reply is not null) values["default_reply_address"] = reply;
        await SaveAsync(values, ct);

        return UiResults.Back(HttpContext, back, success: "Email settings updated.");
    }

    // --- Public tickets (guest policy) ---

    [HttpGet("public-tickets")]
    [EscalatedPage(Negotiate = true)]
    public async Task<IActionResult> PublicTickets(CancellationToken ct)
    {
        var userId = await _settings.GetAsync("guest_policy_user_id", string.Empty, ct);
        return UiResults.Render("Escalated/Admin/Settings/PublicTickets", new
        {
            settings = new Dictionary<string, object?>
            {
                ["guest_policy_mode"] = await SettingAsync("guest_policy_mode", "unassigned", ct),
                ["guest_policy_user_id"] = string.IsNullOrWhiteSpace(userId) ? null : userId.Trim(),
                ["guest_policy_signup_url_template"] = await _settings.GetAsync("guest_policy_signup_url_template", string.Empty, ct) ?? string.Empty,
            },
        });
    }

    /// <summary>The page posts; Laravel's route is a PUT, so both are accepted.</summary>
    [HttpPost("public-tickets")]
    public Task<IActionResult> UpdatePublicTicketsPost(CancellationToken ct) => UpdatePublicTicketsAsync(ct);

    [HttpPut("public-tickets")]
    [EscalatedPage(Negotiate = true)]
    public Task<IActionResult> UpdatePublicTicketsPut(CancellationToken ct) => UpdatePublicTicketsAsync(ct);

    private async Task<IActionResult> UpdatePublicTicketsAsync(CancellationToken ct)
    {
        var input = await UiInput.ReadAsync(Request, ct);
        var errors = new Dictionary<string, string>();

        var mode = input.String("guest_policy_mode");
        if (mode is null) errors["guest_policy_mode"] = "The guest policy mode field is required.";
        else if (!GuestPolicyModes.Contains(mode)) errors["guest_policy_mode"] = "The selected guest policy mode is invalid.";

        var userId = input.String("guest_policy_user_id")?.Trim();
        // Laravel: an integer of at least 1. Host user ids here are strings, so an id
        // that is not a number (a GUID) is kept as it is; a number must be at least 1.
        if (userId is not null && long.TryParse(userId, out var id) && id < 1)
        {
            errors["guest_policy_user_id"] = "The guest policy user id field must be at least 1.";
        }
        else if (mode == "guest_user" && userId is null)
        {
            errors["guest_policy_user_id"] = "The guest policy user id field is required when guest policy mode is guest_user.";
        }

        var template = input.String("guest_policy_signup_url_template");
        if (template is { Length: > 500 })
        {
            errors["guest_policy_signup_url_template"] = "The guest policy signup url template field must not be greater than 500 characters.";
        }

        var back = Page("public-tickets");
        if (errors.Count > 0) return UiResults.Back(HttpContext, back, errors: errors);

        await SaveAsync(new Dictionary<string, string?>
        {
            ["guest_policy_mode"] = mode,
            ["guest_policy_user_id"] = mode == "guest_user" ? userId : string.Empty,
            ["guest_policy_signup_url_template"] = mode == "prompt_signup" ? template ?? string.Empty : string.Empty,
        }, ct);

        return UiResults.Back(HttpContext, back, success: "Guest policy updated.");
    }
}
