using System.Text.Json.Serialization;
using Escalated.Data;
using Escalated.Models;
using Escalated.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Controllers.Admin;

/// <summary>
/// Surface enough of the host User table for an admin to grant or revoke
/// agent / admin access from the panel.
///
/// The .NET plugin does not own a host User entity (the host app does),
/// so we read identity (name + email + id) through the host-supplied
/// <see cref="IUserDirectory"/> and store the admin/agent flags in the
/// plugin's own <see cref="RoleUser"/> table against two well-known role
/// slugs:
/// <list type="bullet">
///   <item><c>escalated-admin</c> — grants admin panel access.</item>
///   <item><c>escalated-agent</c> — grants agent ticket access.</item>
/// </list>
/// This mirrors the canonical Laravel reference
/// (<c>escalated-laravel#94</c>) — the payload emitted to / accepted by
/// the shared Vue page (<c>Escalated/Admin/Users/Index</c>) is unchanged.
/// Hosts wiring the gates differently (Identity claims, Spatie-style
/// custom pivots) can override this controller in their own routes.
/// </summary>
[ApiController]
[Route("support/admin/users")]
public class AdminUsersController : ControllerBase
{
    public const string AdminRoleSlug = "escalated-admin";
    public const string AgentRoleSlug = "escalated-agent";

    private readonly EscalatedDbContext _db;
    private readonly IUserDirectory _directory;

    public AdminUsersController(EscalatedDbContext db, IUserDirectory directory)
    {
        _db = db;
        _directory = directory;
    }

    /// <summary>
    /// GET /support/admin/users — paged + searchable list of host users, each
    /// row annotated with the current admin/agent flags. Shape matches the
    /// props the shared Vue page (<c>Escalated/Admin/Users/Index</c>) expects:
    /// <c>{ data: [{id,name,email,is_admin,is_agent}], current_page, per_page, total, last_page }</c>.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Index(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int? currentUserId = null,
        CancellationToken ct = default)
    {
        const int perPage = 20;
        var safePage = Math.Max(1, page);
        var term = (search ?? string.Empty).Trim();

        var dirPage = await _directory.ListAsync(string.IsNullOrEmpty(term) ? null : term, safePage, perPage, ct);

        // One round-trip to read the admin/agent role assignments for the page.
        var ids = dirPage.Items.Select(u => u.Id).ToList();
        var roleRows = ids.Count == 0
            ? new List<RoleAssignment>()
            : await _db.RoleUsers
                .Where(ru => ids.Contains(ru.UserId)
                    && ru.Role != null
                    && (ru.Role.Slug == AdminRoleSlug || ru.Role.Slug == AgentRoleSlug))
                .Select(ru => new RoleAssignment(ru.UserId, ru.Role!.Slug))
                .ToListAsync(ct);

        var adminIds = roleRows.Where(r => r.Slug == AdminRoleSlug).Select(r => r.UserId).ToHashSet();
        var agentIds = roleRows.Where(r => r.Slug == AgentRoleSlug).Select(r => r.UserId).ToHashSet();

        var rows = dirPage.Items
            .Select(u => new UserRow(
                Id: u.Id,
                Name: u.Name,
                Email: u.Email,
                // Admins are agents too. Mirror Laravel's contract so the Vue
                // page renders the agent badge even if only the admin role was
                // explicitly granted.
                IsAdmin: adminIds.Contains(u.Id),
                IsAgent: agentIds.Contains(u.Id) || adminIds.Contains(u.Id)))
            .ToList();

        // Re-sort: admins first, then agents, then by id ascending. The host
        // directory cannot know the role state without a join into our table,
        // so we do the final ordering here.
        rows = rows
            .OrderByDescending(r => r.IsAdmin)
            .ThenByDescending(r => r.IsAgent)
            .ThenBy(r => r.Id)
            .ToList();

        var total = dirPage.Total;
        var lastPage = total == 0 ? 1 : (int)Math.Ceiling(total / (double)perPage);

        return Ok(new IndexResponse(
            Users: new PaginatedUsers(rows, safePage, perPage, total, lastPage),
            Filters: new IndexFilters(term),
            CurrentUserId: currentUserId));
    }

    /// <summary>Top-level shape consumed by <c>Escalated/Admin/Users/Index</c>.</summary>
    public record IndexResponse(
        [property: JsonPropertyName("users")] PaginatedUsers Users,
        [property: JsonPropertyName("filters")] IndexFilters Filters,
        [property: JsonPropertyName("currentUserId")] int? CurrentUserId);

    public record IndexFilters([property: JsonPropertyName("search")] string Search);

    /// <summary>
    /// Laravel-style paginator wrapper — keys match the keys
    /// <c>->paginate()->through()</c> emits so the shared Vue page renders
    /// against any host with the same template.
    /// </summary>
    public record PaginatedUsers(
        [property: JsonPropertyName("data")] IReadOnlyList<UserRow> Data,
        [property: JsonPropertyName("current_page")] int CurrentPage,
        [property: JsonPropertyName("per_page")] int PerPage,
        [property: JsonPropertyName("total")] int Total,
        [property: JsonPropertyName("last_page")] int LastPage);

    /// <summary>
    /// PATCH /support/admin/users/{userId}/role — flips one role at a time.
    /// <list type="bullet">
    ///   <item><c>role=admin, value=true</c> grants admin AND agent (admins are agents).</item>
    ///   <item><c>role=admin, value=false</c> revokes admin only — leaves agent intact.</item>
    ///   <item><c>role=agent, value=true</c> grants agent only.</item>
    ///   <item><c>role=agent, value=false</c> revokes agent — and, if the target is also
    ///         admin, revokes admin in the same step (otherwise the admin gate would stay
    ///         on while the agent gate was off, which is confusing).</item>
    /// </list>
    /// An admin cannot remove their own admin role — the request is rejected so they
    /// can't lock themselves out of the panel they're using.
    /// </summary>
    [HttpPatch("{userId:int}/role")]
    public async Task<IActionResult> UpdateRole(
        int userId,
        [FromBody] UpdateRoleRequest request,
        [FromQuery] int? currentUserId = null,
        CancellationToken ct = default)
    {
        if (request is null || (request.Role != "admin" && request.Role != "agent"))
        {
            return BadRequest(new { error = "Role must be 'admin' or 'agent'." });
        }

        var target = await _directory.FindAsync(userId, ct);
        if (target is null)
        {
            return NotFound(new { error = "User not found." });
        }

        // Don't let an admin demote themselves and lock themselves out of
        // the admin panel they're trying to use.
        if (request.Role == "admin"
            && !request.Value
            && currentUserId.HasValue
            && currentUserId.Value == target.Id)
        {
            return BadRequest(new { error = "You cannot remove your own admin role." });
        }

        var adminRole = await EnsureRoleAsync(AdminRoleSlug, "Escalated Admin", ct);
        var agentRole = await EnsureRoleAsync(AgentRoleSlug, "Escalated Agent", ct);

        var hadAdmin = await HasRoleAsync(target.Id, adminRole.Id, ct);

        if (request.Role == "admin")
        {
            await SetRoleAsync(target.Id, adminRole.Id, request.Value, ct);
            // Admins are agents; flipping admin off does not also revoke agent
            // (an ex-admin can still answer tickets unless explicitly demoted).
            if (request.Value)
            {
                await SetRoleAsync(target.Id, agentRole.Id, true, ct);
            }
        }
        else
        {
            await SetRoleAsync(target.Id, agentRole.Id, request.Value, ct);
            if (!request.Value && hadAdmin)
            {
                // Revoking agent from an admin would leave the admin gate on
                // but the agent gate off — confusing. Demote them fully.
                await SetRoleAsync(target.Id, adminRole.Id, false, ct);
            }
        }

        await _db.SaveChangesAsync(ct);

        return Ok(new { message = "User updated." });
    }

    private async Task<Role> EnsureRoleAsync(string slug, string name, CancellationToken ct)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Slug == slug, ct);
        if (role is not null) return role;

        role = new Role
        {
            Name = name,
            Slug = slug,
            IsSystem = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        _db.Roles.Add(role);
        await _db.SaveChangesAsync(ct);
        return role;
    }

    private Task<bool> HasRoleAsync(int userId, int roleId, CancellationToken ct)
        => _db.RoleUsers.AnyAsync(ru => ru.UserId == userId && ru.RoleId == roleId, ct);

    private async Task SetRoleAsync(int userId, int roleId, bool value, CancellationToken ct)
    {
        var existing = await _db.RoleUsers
            .FirstOrDefaultAsync(ru => ru.UserId == userId && ru.RoleId == roleId, ct);

        if (value && existing is null)
        {
            _db.RoleUsers.Add(new RoleUser { UserId = userId, RoleId = roleId });
        }
        else if (!value && existing is not null)
        {
            _db.RoleUsers.Remove(existing);
        }
    }

    /// <summary>Payload shape mirrors the Laravel <c>{role, value}</c> contract.</summary>
    public record UpdateRoleRequest(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("value")] bool Value);

    private record RoleAssignment(int UserId, string Slug);

    /// <summary>Row shape returned to the shared Vue page.</summary>
    public record UserRow(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("email")] string? Email,
        [property: JsonPropertyName("is_admin")] bool IsAdmin,
        [property: JsonPropertyName("is_agent")] bool IsAgent);
}
