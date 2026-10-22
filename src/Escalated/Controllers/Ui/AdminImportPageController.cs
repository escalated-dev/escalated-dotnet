using System.Text.Json;
using Escalated.Authorization;
using Escalated.Configuration;
using Escalated.Data;
using Escalated.Models;
using Escalated.Services;
using Escalated.Ui;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Escalated.Controllers.Ui;

/// <summary>
/// The admin Import wizard, as escalated-laravel's <c>Admin\ImportController</c>, on
/// its paths and with its props:
/// <list type="bullet">
/// <item><c>GET import</c>: Index (adapters, the 20 newest jobs)</item>
/// <item><c>GET import/{platform}/connect</c>: Connect; <c>POST .../test-connection</c> answers <c>{success[, error]}</c></item>
/// <item><c>GET import/{platform}/mapping</c>: Mapping (defaults and source fields per entity type)</item>
/// <item><c>POST import/{platform}/review</c>: Review (writes nothing); <c>POST .../start</c> creates the job and queues it</item>
/// <item><c>GET import/progress/{job}</c>: Progress, polled through <c>GET import/status/{job}</c>; <c>POST import/{pause,resume,cancel}/{job}</c></item>
/// </list>
/// The JSON admin API answers <c>GET import</c> too, so Index needs a page request.
/// Tested credentials wait, sealed, in an HttpOnly cookie scoped to the import
/// paths until the import starts, and are never sent in a page.
/// </summary>
[Route("support/admin/import")]
[Authorize(Policy = EscalatedPolicies.Admin)]
[EscalatedPage]
public class AdminImportPageController : ControllerBase
{
    private readonly EscalatedDbContext _db;
    private readonly ImportService _imports;
    private readonly IImportDispatcher _dispatcher;
    private readonly EscalatedOptions _options;

    public AdminImportPageController(EscalatedDbContext db, ImportService imports, IImportDispatcher dispatcher,
        IOptions<EscalatedOptions> options)
    {
        _db = db;
        _imports = imports;
        _dispatcher = dispatcher;
        _options = options.Value;
    }

    private string Base => "/" + _options.RoutePrefix.Trim('/') + "/admin/import";

    [HttpGet]
    [EscalatedPage(Negotiate = true)]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var jobs = await _db.ImportJobs.AsNoTracking().OrderByDescending(j => j.CreatedAt).Take(20).ToListAsync(ct);
        return UiResults.Render("Escalated/Admin/Import/Index", new Dictionary<string, object?>
        {
            ["adapters"] = _imports.Adapters.Select(a => new Dictionary<string, object?>
            {
                ["name"] = a.Name,
                ["display_name"] = a.DisplayName,
                ["credential_fields"] = a.CredentialFields,
            }).ToList(),
            ["jobs"] = jobs.Select(JobProps).ToList(),
        });
    }

    [HttpGet("{platform}/connect")]
    public IActionResult Connect(string platform)
    {
        if (_imports.ResolveAdapter(platform) is not { } adapter) return NotFound();
        return UiResults.Render("Escalated/Admin/Import/Connect", new Dictionary<string, object?>
        {
            ["platform"] = adapter.Name,
            ["display_name"] = adapter.DisplayName,
            ["credential_fields"] = adapter.CredentialFields,
        });
    }

    [HttpPost("{platform}/test-connection")]
    public async Task<IActionResult> TestConnection(string platform, CancellationToken ct)
    {
        if (_imports.ResolveAdapter(platform) is not { } adapter) return NotFound();

        var input = await UiInput.ReadAsync(Request, ct);
        var credentials = new Dictionary<string, string>();
        if (input.Object("credentials") is { ValueKind: JsonValueKind.Object } posted)
        {
            foreach (var field in posted.EnumerateObject())
            {
                credentials[field.Name] = field.Value.ValueKind switch
                {
                    JsonValueKind.String => field.Value.GetString() ?? "",
                    JsonValueKind.Null => "",
                    _ => field.Value.GetRawText(),
                };
            }
        }

        bool success;
        try
        {
            success = await adapter.TestConnectionAsync(JsonSerializer.Serialize(credentials), ct);
        }
        catch (Exception e)
        {
            return StatusCode(StatusCodes.Status422UnprocessableEntity, new { success = false, error = e.Message });
        }

        if (success)
        {
            Response.Cookies.Append(CookieName(adapter), _imports.Credentials.Seal(credentials), CookieOptions(TimeSpan.FromHours(2)));
        }

        return Ok(new { success });
    }

    [HttpGet("{platform}/mapping")]
    public async Task<IActionResult> Mapping(string platform, CancellationToken ct)
    {
        if (_imports.ResolveAdapter(platform) is not { } adapter) return NotFound();
        if (StoredCredentials(adapter) is not { } credentials) return ToConnect(adapter);

        var json = JsonSerializer.Serialize(credentials);
        var mappings = new Dictionary<string, object?>();
        foreach (var type in adapter.EntityTypes)
        {
            mappings[type] = new Dictionary<string, object?>
            {
                ["defaults"] = adapter.DefaultFieldMappings(type),
                ["source_fields"] = await adapter.AvailableSourceFieldsAsync(type, json, ct),
            };
        }

        return UiResults.Render("Escalated/Admin/Import/Mapping", new Dictionary<string, object?>
        {
            ["platform"] = adapter.Name,
            ["display_name"] = adapter.DisplayName,
            ["entity_types"] = adapter.EntityTypes,
            ["mappings"] = mappings,
        });
    }

    [HttpPost("{platform}/review")]
    public async Task<IActionResult> Review(string platform, CancellationToken ct)
    {
        var input = await UiInput.ReadAsync(Request, ct);
        return UiResults.Render("Escalated/Admin/Import/Review", new Dictionary<string, object?>
        {
            ["platform"] = platform,
            ["field_mappings"] = input.Object("field_mappings") is { ValueKind: JsonValueKind.Object } m ? m : new Dictionary<string, object?>(),
            ["entity_types"] = input.List("entity_types"),
        });
    }

    [HttpPost("{platform}/start")]
    public async Task<IActionResult> Start(string platform, CancellationToken ct)
    {
        if (_imports.ResolveAdapter(platform) is not { } adapter) return NotFound();
        if (StoredCredentials(adapter) is not { } credentials) return ToConnect(adapter);

        var input = await UiInput.ReadAsync(Request, ct);
        var job = new ImportJob
        {
            UserId = this.CurrentUserId(),
            Platform = adapter.Name,
            Status = "mapping",
            Credentials = _imports.Credentials.Seal(credentials),
            FieldMappings = input.Object("field_mappings") is { ValueKind: JsonValueKind.Object } m ? m.GetRawText() : "{}",
            Progress = "{}",
            Errors = "[]",
        };
        _db.ImportJobs.Add(job);
        await _db.SaveChangesAsync(ct);

        // The job holds them now, sealed.
        Response.Cookies.Delete(CookieName(adapter), CookieOptions(null));
        _dispatcher.Dispatch(job.Id);

        return new UiRedirectResult($"{Base}/progress/{job.Id}");
    }

    [HttpGet("progress/{jobId:guid}")]
    public async Task<IActionResult> Progress(Guid jobId, CancellationToken ct)
    {
        var job = await _db.ImportJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null) return NotFound();
        return UiResults.Render("Escalated/Admin/Import/Progress", new Dictionary<string, object?> { ["job"] = JobProps(job) });
    }

    [HttpGet("status/{jobId:guid}")]
    public async Task<IActionResult> Status(Guid jobId, CancellationToken ct)
    {
        var job = await _db.ImportJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null) return NotFound();
        var errors = ImportService.ReadErrors(job);
        return Ok(new Dictionary<string, object?>
        {
            ["status"] = job.Status,
            ["progress"] = ImportService.ReadProgress(job),
            ["error_log"] = errors.Skip(Math.Max(0, errors.Count - 50)).ToList(),
            ["started_at"] = job.StartedAt,
            ["completed_at"] = job.CompletedAt,
        });
    }

    [HttpPost("pause/{jobId:guid}")]
    public async Task<IActionResult> Pause(Guid jobId, CancellationToken ct)
    {
        var job = await _db.ImportJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null) return NotFound();
        if (job.Status == "importing")
        {
            job.Status = "paused";
            job.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
        return ToProgress(job, "Import paused.");
    }

    [HttpPost("resume/{jobId:guid}")]
    public async Task<IActionResult> Resume(Guid jobId, CancellationToken ct)
    {
        var job = await _db.ImportJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null) return NotFound();
        if (job.IsResumable)
        {
            // A failed job goes back through mapping, as Laravel's does.
            if (job.Status == "failed")
            {
                job.TransitionTo("mapping");
                await _db.SaveChangesAsync(ct);
            }
            _dispatcher.Dispatch(job.Id);
        }
        return ToProgress(job, "Import resumed.");
    }

    [HttpPost("cancel/{jobId:guid}")]
    public async Task<IActionResult> Cancel(Guid jobId, CancellationToken ct)
    {
        var job = await _db.ImportJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null) return NotFound();
        job.Status = "failed";
        job.CompletedAt = DateTime.UtcNow;
        job.Credentials = null;
        job.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return ToProgress(job, "Import cancelled.");
    }

    /// <summary>
    /// The job as the screens read it. Laravel sends the model as is; its credentials
    /// are left out here: they are a secret, and no screen reads them.
    /// </summary>
    private static Dictionary<string, object?> JobProps(ImportJob job) => new()
    {
        ["id"] = job.Id,
        ["user_id"] = job.UserId,
        ["platform"] = job.Platform,
        ["status"] = job.Status,
        ["field_mappings"] = string.IsNullOrEmpty(job.FieldMappings) ? new Dictionary<string, object?>() : JsonSerializer.Deserialize<JsonElement>(job.FieldMappings),
        ["progress"] = ImportService.ReadProgress(job),
        ["error_log"] = ImportService.ReadErrors(job),
        ["started_at"] = job.StartedAt,
        ["completed_at"] = job.CompletedAt,
        ["created_at"] = job.CreatedAt,
        ["updated_at"] = job.UpdatedAt,
    };

    private static string CookieName(IImportAdapter adapter) =>
        "escalated_import_" + new string(adapter.Name.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

    private CookieOptions CookieOptions(TimeSpan? maxAge) => new()
    {
        HttpOnly = true,
        Secure = Request.IsHttps,
        SameSite = SameSiteMode.Lax,
        Path = Base,
        MaxAge = maxAge,
    };

    private Dictionary<string, string>? StoredCredentials(IImportAdapter adapter) =>
        Request.Cookies.TryGetValue(CookieName(adapter), out var token) ? _imports.Credentials.Open(token) : null;

    private IActionResult ToConnect(IImportAdapter adapter) =>
        new UiRedirectResult($"{Base}/{adapter.Name}/connect") { Error = "Credentials expired. Please reconnect." };

    private IActionResult ToProgress(ImportJob job, string message) =>
        new UiRedirectResult($"{Base}/progress/{job.Id}") { Success = message };
}
