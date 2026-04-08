using Escalated.Data;
using Escalated.Models;
using Escalated.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Controllers.Admin;

[ApiController]
[Route("support/admin/import")]
public class AdminImportController : ControllerBase
{
    private readonly ImportService _importService;
    private readonly EscalatedDbContext _db;

    public AdminImportController(ImportService importService, EscalatedDbContext db)
    {
        _importService = importService;
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var jobs = await _db.ImportJobs.OrderByDescending(j => j.CreatedAt).ToListAsync();
        return Ok(jobs);
    }

    [HttpPost("create")]
    public async Task<IActionResult> Create([FromBody] CreateImportRequest request)
    {
        var job = new ImportJob
        {
            Platform = request.Platform,
            Status = "pending",
            Credentials = request.Credentials,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.ImportJobs.Add(job);
        await _db.SaveChangesAsync();
        return Ok(job);
    }

    [HttpPost("{id:guid}/test")]
    public async Task<IActionResult> TestConnection(Guid id)
    {
        var job = await _db.ImportJobs.FindAsync(id);
        if (job == null) return NotFound();

        var result = await _importService.TestConnectionAsync(job);
        return Ok(new { success = result });
    }

    [HttpPost("{id:guid}/mapping")]
    public async Task<IActionResult> SaveMapping(Guid id, [FromBody] SaveMappingRequest request)
    {
        var job = await _db.ImportJobs.FindAsync(id);
        if (job == null) return NotFound();

        job.FieldMappings = request.Mappings;
        job.TransitionTo("mapping");
        _db.ImportJobs.Update(job);
        await _db.SaveChangesAsync();
        return Ok(job);
    }

    [HttpPost("{id:guid}/run")]
    public async Task<IActionResult> Run(Guid id)
    {
        var job = await _db.ImportJobs.FindAsync(id);
        if (job == null) return NotFound();

        // Run in background in a real implementation
        await _importService.RunAsync(job);
        return Ok(job);
    }

    [HttpPost("{id:guid}/pause")]
    public async Task<IActionResult> Pause(Guid id)
    {
        var job = await _db.ImportJobs.FindAsync(id);
        if (job == null) return NotFound();

        job.Status = "paused";
        _db.ImportJobs.Update(job);
        await _db.SaveChangesAsync();
        return Ok(job);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Show(Guid id)
    {
        var job = await _db.ImportJobs.FindAsync(id);
        if (job == null) return NotFound();
        return Ok(job);
    }
}

public record CreateImportRequest(string Platform, string? Credentials = null);
public record SaveMappingRequest(string Mappings);
