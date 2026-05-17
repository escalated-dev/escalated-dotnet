using System.Text.Json.Serialization;
using Escalated.Data;
using Escalated.Dtos.Admin;
using Escalated.Models;
using Escalated.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Controllers.Admin;

[ApiController]
[Route("support/admin/skills")]
public class AdminSkillsController : ControllerBase
{
    private readonly EscalatedDbContext _db;
    private readonly IUserDirectory _directory;

    public AdminSkillsController(EscalatedDbContext db, IUserDirectory directory)
    {
        _db = db;
        _directory = directory;
    }

    /// <summary>GET /support/admin/skills</summary>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct = default)
    {
        var skills = await _db.Skills
            .AsNoTracking()
            .OrderBy(s => s.Name)
            .Select(s => new SkillIndexRow(
                s.Id,
                s.Name,
                s.AgentSkills.Count,
                s.RoutingTags.Count,
                s.RoutingDepartments.Count,
                s.UpdatedAt))
            .ToListAsync(ct);

        return Ok(new SkillsIndexEnvelope(skills));
    }

    /// <summary>GET /support/admin/skills/create</summary>
    [HttpGet("create")]
    public async Task<IActionResult> Create(CancellationToken ct = default)
    {
        var available = await BuildAvailableLookups(ct);
        return Ok(new SkillEditEnvelope(
            Skill: EmptySkillDraft(),
            available.AvailableAgents,
            available.AvailableTags,
            available.AvailableDepartments));
    }

    /// <summary>POST /support/admin/skills</summary>
    [HttpPost]
    public async Task<IActionResult> Store([FromBody] CreateSkillDto dto, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var validationErr = await ValidateRoutingIdsAsync(dto.RoutingTagIds, dto.RoutingDepartmentIds, ct);
        if (validationErr is not null)
        {
            return BadRequest(new { error = validationErr });
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var now = DateTime.UtcNow;
        var slug = await ResolveUniqueSlugAsync(Skill.GenerateSlug(dto.Name.Trim()), null, ct);

        var skill = new Skill
        {
            Name = dto.Name.Trim(),
            Slug = slug,
            Description = dto.Description?.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.Skills.Add(skill);
        await _db.SaveChangesAsync(ct);

        await ReplaceAssignmentsAsync(skill.Id, dto.RoutingTagIds, dto.RoutingDepartmentIds, dto.Agents, now, ct);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Ok(new CreateSkillEnvelope(skill.Id));
    }

    /// <summary>GET /support/admin/skills/{id}/edit</summary>
    [HttpGet("{id:int}/edit")]
    public async Task<IActionResult> Edit(int id, CancellationToken ct = default)
    {
        var skill = await _db.Skills
            .AsNoTracking()
            .Include(s => s.AgentSkills)
            .Include(s => s.RoutingTags)
            .Include(s => s.RoutingDepartments)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

        if (skill is null)
        {
            return NotFound(new { error = "Skill not found." });
        }

        var lookups = await BuildAvailableLookups(ct);
        var envelope = SkillEditEnvelope.From(skill, lookups);
        return Ok(envelope);
    }

    /// <summary>PUT /support/admin/skills/{id}</summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateSkillDto dto, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var validationErr = await ValidateRoutingIdsAsync(dto.RoutingTagIds, dto.RoutingDepartmentIds, ct);
        if (validationErr is not null)
        {
            return BadRequest(new { error = validationErr });
        }

        var skill = await _db.Skills.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (skill is null)
        {
            return NotFound(new { error = "Skill not found." });
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        skill.Name = dto.Name.Trim();
        skill.Description = dto.Description?.Trim();

        skill.Slug = await ResolveUniqueSlugAsync(Skill.GenerateSlug(skill.Name), skill.Id, ct);

        skill.UpdatedAt = DateTime.UtcNow;

        await ReplaceAssignmentsAsync(skill.Id, dto.RoutingTagIds, dto.RoutingDepartmentIds, dto.Agents, skill.UpdatedAt, ct);

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Ok(new { message = "Skill updated." });
    }

    /// <summary>DELETE /support/admin/skills/{id}</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Destroy(int id, CancellationToken ct = default)
    {
        var skill = await _db.Skills.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (skill is null)
        {
            return NotFound(new { error = "Skill not found." });
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await ClearSkillAssignmentsAsync(id, ct);
        _db.Skills.Remove(skill);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Ok(new { message = "Skill deleted." });
    }

    private async Task ClearSkillAssignmentsAsync(int skillId, CancellationToken ct)
    {
        var tags = await _db.SkillRoutingTags.Where(r => r.SkillId == skillId).ToListAsync(ct);
        var deptRows = await _db.SkillRoutingDepartments.Where(r => r.SkillId == skillId).ToListAsync(ct);
        var entries = await _db.AgentSkills.Where(a => a.SkillId == skillId).ToListAsync(ct);

        _db.SkillRoutingTags.RemoveRange(tags);
        _db.SkillRoutingDepartments.RemoveRange(deptRows);
        _db.AgentSkills.RemoveRange(entries);
    }

    private async Task<(IReadOnlyList<AgentPick> AvailableAgents, IReadOnlyList<TagPick> AvailableTags, IReadOnlyList<DeptPick> AvailableDepartments)> BuildAvailableLookups(
        CancellationToken ct)
    {
        var tags = await _db.Tags.AsNoTracking()
            .OrderBy(t => t.Name)
            .Select(t => new TagPick(t.Id, t.Name))
            .ToListAsync(ct);

        var departments = await _db.Departments.AsNoTracking()
            .Where(d => d.IsActive)
            .OrderBy(d => d.Name)
            .Select(d => new DeptPick(d.Id, d.Name))
            .ToListAsync(ct);

        var agentRoleIds = await _db.RoleUsers
            .Where(ru => ru.Role != null
                && (ru.Role!.Slug == AdminUsersController.AgentRoleSlug
                    || ru.Role.Slug == AdminUsersController.AdminRoleSlug))
            .Select(ru => ru.UserId)
            .Distinct()
            .ToListAsync(ct);

        var picks = new List<AgentPick>();
        foreach (var userId in agentRoleIds.OrderBy(i => i))
        {
            var entry = await _directory.FindAsync(userId, ct);
            if (entry is not null)
            {
                picks.Add(new AgentPick(entry.Id, entry.Name, entry.Email));
            }
        }

        return (picks, tags, departments);
    }

    private static SkillDetailDto EmptySkillDraft() =>
        new(0, string.Empty, null, [], [], []);

    private async Task ReplaceAssignmentsAsync(
        int skillId,
        int[]? routingTagIds,
        int[]? routingDepartmentIds,
        AgentSkillEntryDto[]? agents,
        DateTime timestamps,
        CancellationToken ct)
    {
        await ClearSkillAssignmentsAsync(skillId, ct);

        var tagDistinct = routingTagIds?.Distinct().ToList() ?? [];
        foreach (var tagId in tagDistinct)
        {
            _db.SkillRoutingTags.Add(new SkillRoutingTag { SkillId = skillId, TagId = tagId });
        }

        var deptDistinct = routingDepartmentIds?.Distinct().ToList() ?? [];
        foreach (var departmentId in deptDistinct)
        {
            _db.SkillRoutingDepartments.Add(new SkillRoutingDepartment { SkillId = skillId, DepartmentId = departmentId });
        }

        if (agents is { Length: > 0 })
        {
            foreach (var row in agents
                         .GroupBy(a => a.UserId)
                         .Select(g => g.Last()))
            {
                _db.AgentSkills.Add(new AgentSkill
                {
                    UserId = row.UserId,
                    SkillId = skillId,
                    Proficiency = row.Proficiency,
                    CreatedAt = timestamps,
                    UpdatedAt = timestamps,
                });
            }
        }
    }

    private async Task<string?> ValidateRoutingIdsAsync(int[]? tagIds, int[]? departmentIds, CancellationToken ct)
    {
        var tagDistinct = tagIds?.Distinct().ToArray() ?? [];
        if (tagDistinct.Length != 0)
        {
            var count = await _db.Tags.Where(t => tagDistinct.Contains(t.Id)).CountAsync(ct);
            if (count != tagDistinct.Length)
            {
                return "One or more routing tag ids were not found.";
            }
        }

        var deptDistinct = departmentIds?.Distinct().ToArray() ?? [];
        if (deptDistinct.Length != 0)
        {
            var count = await _db.Departments.Where(d => deptDistinct.Contains(d.Id)).CountAsync(ct);
            if (count != deptDistinct.Length)
            {
                return "One or more routing department ids were not found.";
            }
        }

        return null;
    }

    private async Task<string> ResolveUniqueSlugAsync(string baseSlug, int? exceptSkillId, CancellationToken ct)
    {
        if (!await IsSlugTakenAsync(baseSlug, exceptSkillId, ct))
        {
            return baseSlug;
        }

        for (var suffix = 2; suffix < int.MaxValue; suffix++)
        {
            var candidate = $"{baseSlug}-{suffix}";
            if (!await IsSlugTakenAsync(candidate, exceptSkillId, ct))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Unable to allocate a unique skill slug.");
    }

    private Task<bool> IsSlugTakenAsync(string slug, int? exceptSkillId, CancellationToken ct)
    {
        return exceptSkillId is null
            ? _db.Skills.AnyAsync(s => s.Slug == slug, ct)
            : _db.Skills.AnyAsync(s => s.Slug == slug && s.Id != exceptSkillId, ct);
    }

#pragma warning disable CA1034 // Nested types acceptable for grouped API contract records

    public sealed record SkillsIndexEnvelope(
        [property: JsonPropertyName("skills")] IReadOnlyList<SkillIndexRow> Skills);

    public sealed record SkillIndexRow(int Id, string Name, int AgentsCount, int RoutingTagsCount, int RoutingDepartmentsCount, DateTime UpdatedAt);

    public sealed record CreateSkillEnvelope([property: JsonPropertyName("id")] int Id);

    /// <summary>Shape returned by Edit and Create for the Vue admin Skills screen.</summary>
    public sealed record SkillEditEnvelope(
        [property: JsonPropertyName("skill")] SkillDetailDto Skill,
        [property: JsonPropertyName("availableAgents")] IReadOnlyList<AgentPick> AvailableAgents,
        [property: JsonPropertyName("availableTags")] IReadOnlyList<TagPick> AvailableTags,
        [property: JsonPropertyName("availableDepartments")] IReadOnlyList<DeptPick> AvailableDepartments)
    {
        public static SkillEditEnvelope From(Skill s, (
            IReadOnlyList<AgentPick> AvailableAgents,
            IReadOnlyList<TagPick> AvailableTags,
            IReadOnlyList<DeptPick> AvailableDepartments) lookups) =>
            new(
                Skill: new SkillDetailDto(
                    s.Id,
                    s.Name,
                    s.Description,
                    s.RoutingTags.Select(rt => rt.TagId).Distinct().OrderBy(i => i).ToArray(),
                    s.RoutingDepartments.Select(rd => rd.DepartmentId).Distinct().OrderBy(i => i).ToArray(),
                    s.AgentSkills
                        .GroupBy(a => a.UserId)
                        .Select(g => g.Last())
                        .OrderBy(r => r.UserId)
                        .Select(r => new AgentSkillAssignmentDto(r.UserId, r.Proficiency))
                        .ToArray()),
                lookups.AvailableAgents,
                lookups.AvailableTags,
                lookups.AvailableDepartments);
    }

    public sealed record SkillDetailDto(
        int Id,
        string Name,
        string? Description,
        int[] RoutingTagIds,
        int[] RoutingDepartmentIds,
        AgentSkillAssignmentDto[] Agents);

    public sealed record AgentSkillAssignmentDto(int UserId, int Proficiency);

    public sealed record AgentPick(int Id, string? Name, string? Email);

    public sealed record TagPick(int Id, string Name);

    public sealed record DeptPick(int Id, string Name);

#pragma warning restore CA1034
}
