using Escalated.Data;
using Escalated.Models;
using Escalated.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Controllers.Admin;

[ApiController]
[Route("support/admin")]
public class AdminSettingsController : ControllerBase
{
    private readonly EscalatedDbContext _db;
    private readonly AuditLogService _auditLogService;
    private readonly SettingsService _settingsService;

    public AdminSettingsController(EscalatedDbContext db, AuditLogService auditLogService,
        SettingsService settingsService)
    {
        _db = db;
        _auditLogService = auditLogService;
        _settingsService = settingsService;
    }

    // --- Departments ---

    [HttpGet("departments")]
    public async Task<IActionResult> Departments()
    {
        var departments = await _db.Departments.OrderBy(d => d.Name).ToListAsync();
        return Ok(departments);
    }

    [HttpPost("departments")]
    public async Task<IActionResult> CreateDepartment([FromBody] Department dept)
    {
        dept.Slug = Department.GenerateSlug(dept.Name);
        dept.CreatedAt = DateTime.UtcNow;
        dept.UpdatedAt = DateTime.UtcNow;
        _db.Departments.Add(dept);
        await _db.SaveChangesAsync();
        return Ok(dept);
    }

    [HttpPut("departments/{id:int}")]
    public async Task<IActionResult> UpdateDepartment(int id, [FromBody] Department update)
    {
        var dept = await _db.Departments.FindAsync(id);
        if (dept == null) return NotFound();
        dept.Name = update.Name;
        dept.Description = update.Description;
        dept.IsActive = update.IsActive;
        dept.UpdatedAt = DateTime.UtcNow;
        _db.Departments.Update(dept);
        await _db.SaveChangesAsync();
        return Ok(dept);
    }

    [HttpDelete("departments/{id:int}")]
    public async Task<IActionResult> DeleteDepartment(int id)
    {
        var dept = await _db.Departments.FindAsync(id);
        if (dept == null) return NotFound();
        _db.Departments.Remove(dept);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // --- Tags ---

    [HttpGet("tags")]
    public async Task<IActionResult> Tags()
    {
        var tags = await _db.Tags.OrderBy(t => t.Name).ToListAsync();
        return Ok(tags);
    }

    [HttpPost("tags")]
    public async Task<IActionResult> CreateTag([FromBody] Tag tag)
    {
        tag.Slug = Tag.GenerateSlug(tag.Name);
        tag.CreatedAt = DateTime.UtcNow;
        tag.UpdatedAt = DateTime.UtcNow;
        _db.Tags.Add(tag);
        await _db.SaveChangesAsync();
        return Ok(tag);
    }

    [HttpDelete("tags/{id:int}")]
    public async Task<IActionResult> DeleteTag(int id)
    {
        var tag = await _db.Tags.FindAsync(id);
        if (tag == null) return NotFound();
        _db.Tags.Remove(tag);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // --- SLA Policies ---

    [HttpGet("sla-policies")]
    public async Task<IActionResult> SlaPolicies()
    {
        var policies = await _db.SlaPolicies.ToListAsync();
        return Ok(policies);
    }

    [HttpPost("sla-policies")]
    public async Task<IActionResult> CreateSlaPolicy([FromBody] SlaPolicy policy)
    {
        policy.CreatedAt = DateTime.UtcNow;
        policy.UpdatedAt = DateTime.UtcNow;
        _db.SlaPolicies.Add(policy);
        await _db.SaveChangesAsync();
        return Ok(policy);
    }

    [HttpPut("sla-policies/{id:int}")]
    public async Task<IActionResult> UpdateSlaPolicy(int id, [FromBody] SlaPolicy update)
    {
        var policy = await _db.SlaPolicies.FindAsync(id);
        if (policy == null) return NotFound();
        policy.Name = update.Name;
        policy.Description = update.Description;
        policy.FirstResponseHours = update.FirstResponseHours;
        policy.ResolutionHours = update.ResolutionHours;
        policy.BusinessHoursOnly = update.BusinessHoursOnly;
        policy.IsDefault = update.IsDefault;
        policy.IsActive = update.IsActive;
        policy.UpdatedAt = DateTime.UtcNow;
        _db.SlaPolicies.Update(policy);
        await _db.SaveChangesAsync();
        return Ok(policy);
    }

    // --- Escalation Rules ---

    [HttpGet("escalation-rules")]
    public async Task<IActionResult> EscalationRules()
    {
        var rules = await _db.EscalationRules.ToListAsync();
        return Ok(rules);
    }

    [HttpPost("escalation-rules")]
    public async Task<IActionResult> CreateEscalationRule([FromBody] EscalationRule rule)
    {
        rule.CreatedAt = DateTime.UtcNow;
        rule.UpdatedAt = DateTime.UtcNow;
        _db.EscalationRules.Add(rule);
        await _db.SaveChangesAsync();
        return Ok(rule);
    }

    [HttpPut("escalation-rules/{id:int}")]
    public async Task<IActionResult> UpdateEscalationRule(int id, [FromBody] EscalationRule update)
    {
        var rule = await _db.EscalationRules.FindAsync(id);
        if (rule == null) return NotFound();
        rule.Name = update.Name;
        rule.Description = update.Description;
        rule.Conditions = update.Conditions;
        rule.Actions = update.Actions;
        rule.IsActive = update.IsActive;
        rule.UpdatedAt = DateTime.UtcNow;
        _db.EscalationRules.Update(rule);
        await _db.SaveChangesAsync();
        return Ok(rule);
    }

    // --- Canned Responses ---

    [HttpGet("canned-responses")]
    public async Task<IActionResult> CannedResponses()
    {
        return Ok(await _db.CannedResponses.ToListAsync());
    }

    [HttpPost("canned-responses")]
    public async Task<IActionResult> CreateCannedResponse([FromBody] CannedResponse response)
    {
        response.CreatedAt = DateTime.UtcNow;
        response.UpdatedAt = DateTime.UtcNow;
        _db.CannedResponses.Add(response);
        await _db.SaveChangesAsync();
        return Ok(response);
    }

    // --- Macros ---

    [HttpGet("macros")]
    public async Task<IActionResult> Macros()
    {
        return Ok(await _db.Macros.OrderBy(m => m.Order).ToListAsync());
    }

    [HttpPost("macros")]
    public async Task<IActionResult> CreateMacro([FromBody] Macro macro)
    {
        macro.CreatedAt = DateTime.UtcNow;
        macro.UpdatedAt = DateTime.UtcNow;
        _db.Macros.Add(macro);
        await _db.SaveChangesAsync();
        return Ok(macro);
    }

    // --- Automations ---

    [HttpGet("automations")]
    public async Task<IActionResult> Automations()
    {
        return Ok(await _db.Automations.OrderBy(a => a.Position).ToListAsync());
    }

    [HttpPost("automations")]
    public async Task<IActionResult> CreateAutomation([FromBody] Automation automation)
    {
        automation.CreatedAt = DateTime.UtcNow;
        automation.UpdatedAt = DateTime.UtcNow;
        _db.Automations.Add(automation);
        await _db.SaveChangesAsync();
        return Ok(automation);
    }

    // --- Webhooks ---

    [HttpGet("webhooks")]
    public async Task<IActionResult> Webhooks()
    {
        return Ok(await _db.Webhooks.ToListAsync());
    }

    [HttpPost("webhooks")]
    public async Task<IActionResult> CreateWebhook([FromBody] Webhook webhook)
    {
        webhook.CreatedAt = DateTime.UtcNow;
        webhook.UpdatedAt = DateTime.UtcNow;
        _db.Webhooks.Add(webhook);
        await _db.SaveChangesAsync();
        return Ok(webhook);
    }

    // --- API Tokens ---

    [HttpGet("api-tokens")]
    public async Task<IActionResult> ApiTokens()
    {
        return Ok(await _db.ApiTokens.Select(t => new
        {
            t.Id,
            t.Name,
            t.Abilities,
            t.LastUsedAt,
            t.ExpiresAt,
            t.CreatedAt
        }).ToListAsync());
    }

    [HttpPost("api-tokens")]
    public async Task<IActionResult> CreateApiToken([FromBody] CreateApiTokenRequest request)
    {
        var plainToken = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        var hash = Middleware.ApiTokenAuthMiddleware.ComputeSha256(plainToken);

        var token = new ApiToken
        {
            Name = request.Name,
            TokenHash = hash,
            Abilities = request.Abilities,
            TokenableType = request.TokenableType,
            TokenableId = request.TokenableId,
            ExpiresAt = request.ExpiresAt,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.ApiTokens.Add(token);
        await _db.SaveChangesAsync();

        // Return the plain token only on creation
        return Ok(new { token.Id, token.Name, plainToken, token.Abilities, token.ExpiresAt });
    }

    [HttpDelete("api-tokens/{id:int}")]
    public async Task<IActionResult> DeleteApiToken(int id)
    {
        var token = await _db.ApiTokens.FindAsync(id);
        if (token == null) return NotFound();
        _db.ApiTokens.Remove(token);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // --- Roles ---

    [HttpGet("roles")]
    public async Task<IActionResult> Roles()
    {
        return Ok(await _db.Roles.Include(r => r.Permissions).ToListAsync());
    }

    [HttpPost("roles")]
    public async Task<IActionResult> CreateRole([FromBody] Role role)
    {
        role.Slug = role.Name.ToLower().Replace(" ", "-");
        role.CreatedAt = DateTime.UtcNow;
        role.UpdatedAt = DateTime.UtcNow;
        _db.Roles.Add(role);
        await _db.SaveChangesAsync();
        return Ok(role);
    }

    // --- Audit Logs ---

    [HttpGet("audit-logs")]
    public async Task<IActionResult> AuditLogs([FromQuery] int? userId, [FromQuery] string? entityType,
        [FromQuery] int page = 1, [FromQuery] int perPage = 50)
    {
        var (items, total) = await _auditLogService.ListAsync(userId, entityType, null, page, perPage);
        return Ok(new { data = items, total, page, perPage });
    }

    // --- Settings ---

    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings()
    {
        var settings = await _settingsService.GetAllAsync();
        return Ok(settings);
    }

    [HttpPost("settings")]
    public async Task<IActionResult> SaveSettings([FromBody] Dictionary<string, string?> settings)
    {
        foreach (var (key, value) in settings)
        {
            await _settingsService.SetAsync(key, value);
        }
        return Ok(new { message = "Settings saved." });
    }

    // --- Custom Fields ---

    [HttpGet("custom-fields")]
    public async Task<IActionResult> CustomFields()
    {
        return Ok(await _db.CustomFields.OrderBy(f => f.Position).ToListAsync());
    }

    [HttpPost("custom-fields")]
    public async Task<IActionResult> CreateCustomField([FromBody] CustomField field)
    {
        field.Slug = field.Name.ToLower().Replace(" ", "_");
        field.CreatedAt = DateTime.UtcNow;
        field.UpdatedAt = DateTime.UtcNow;
        _db.CustomFields.Add(field);
        await _db.SaveChangesAsync();
        return Ok(field);
    }

    // --- Business Schedules ---

    [HttpGet("business-hours")]
    public async Task<IActionResult> BusinessHours()
    {
        return Ok(await _db.BusinessSchedules.Include(b => b.Holidays).ToListAsync());
    }

    [HttpPost("business-hours")]
    public async Task<IActionResult> CreateBusinessSchedule([FromBody] BusinessSchedule schedule)
    {
        schedule.CreatedAt = DateTime.UtcNow;
        schedule.UpdatedAt = DateTime.UtcNow;
        _db.BusinessSchedules.Add(schedule);
        await _db.SaveChangesAsync();
        return Ok(schedule);
    }

    // --- Skills ---

    [HttpGet("skills")]
    public async Task<IActionResult> Skills()
    {
        return Ok(await _db.Skills.Include(s => s.AgentSkills).ToListAsync());
    }

    [HttpPost("skills")]
    public async Task<IActionResult> CreateSkill([FromBody] Skill skill)
    {
        skill.Slug = Skill.GenerateSlug(skill.Name);
        skill.CreatedAt = DateTime.UtcNow;
        skill.UpdatedAt = DateTime.UtcNow;
        _db.Skills.Add(skill);
        await _db.SaveChangesAsync();
        return Ok(skill);
    }

    // --- Capacity ---

    [HttpGet("capacity")]
    public async Task<IActionResult> Capacity([FromServices] CapacityService capacityService)
    {
        return Ok(await capacityService.GetAllCapacitiesAsync());
    }

    // --- Custom Objects ---

    [HttpGet("custom-objects")]
    public async Task<IActionResult> CustomObjects()
    {
        return Ok(await _db.CustomObjects.ToListAsync());
    }

    [HttpPost("custom-objects")]
    public async Task<IActionResult> CreateCustomObject([FromBody] CustomObject obj)
    {
        obj.Slug = obj.Name.ToLower().Replace(" ", "_");
        obj.CreatedAt = DateTime.UtcNow;
        obj.UpdatedAt = DateTime.UtcNow;
        _db.CustomObjects.Add(obj);
        await _db.SaveChangesAsync();
        return Ok(obj);
    }
}

public record CreateApiTokenRequest(string Name, string? Abilities = null, string? TokenableType = null,
    int? TokenableId = null, DateTime? ExpiresAt = null);
