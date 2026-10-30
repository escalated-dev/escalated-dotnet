using System.Text.Json;
using System.Text.Json.Serialization;
using Escalated.Data;
using Escalated.Models;
using Escalated.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Escalated.Authorization;
using Escalated.Configuration;
using Escalated.Ui;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

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
///
/// <para>With <c>Escalated:Ui:Enabled</c> on, a page request (an Inertia visit or a
/// browser GET) gets the page itself, and a page's role toggle a 303 back with a
/// flash, as escalated-laravel's <c>UserController</c> answers. JSON callers are
/// unchanged.</para>
/// </summary>
[ApiController]
[Route("support/admin/users")]
[Authorize(Policy = EscalatedPolicies.Admin)]
public class AdminUsersController : ControllerBase
{
    public const string AdminRoleSlug = "escalated-admin";
    public const string AgentRoleSlug = "escalated-agent";

    private readonly EscalatedDbContext _db;
    private readonly IUserDirectory _directory;
    private readonly EscalatedOptions _options;
    private readonly AccessFeatures _features;

    /// <summary>
    /// The gate roles: who reaches the admin and agent areas. They are not Escalated roles
    /// (they hold no permissions), so the roles picker neither lists nor changes them.
    /// </summary>
    private static readonly string[] GateRoleSlugs = { AdminRoleSlug, AgentRoleSlug };

    public AdminUsersController(EscalatedDbContext db, IUserDirectory directory, IOptions<EscalatedOptions>? options = null,
        AccessFeatures? features = null)
    {
        _db = db;
        _directory = directory;
        _options = options?.Value ?? new EscalatedOptions();
        _features = features ?? new AccessFeatures(Options.Create(_options));
    }

    /// <summary>A request for the page rather than the JSON, with the UI on.</summary>
    private bool IsPage => _options.Ui.Enabled && EscalatedPageAttribute.IsPageRequest(Request);

    private string IndexUrl => $"/{_options.RoutePrefix.Trim('/')}/admin/users";

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

        if (IsPage)
        {
            var query = new Dictionary<string, string?> { ["search"] = string.IsNullOrEmpty(term) ? null : term };
            var pageRows = rows.Cast<object?>().ToList();
            object? roleOptions = null;

            // Escalated roles, only while roles & permissions is enforced: each user's role
            // slugs, and the roles an admin can give them (escalated-laravel's UserController).
            if (_features.RolesPermissions)
            {
                var held = ids.Count == 0
                    ? new List<RoleAssignment>()
                    : await _db.RoleUsers.AsNoTracking()
                        .Where(ru => ids.Contains(ru.UserId) && ru.Role != null && !GateRoleSlugs.Contains(ru.Role.Slug))
                        .Select(ru => new RoleAssignment(ru.UserId, ru.Role!.Slug))
                        .ToListAsync(ct);
                pageRows = rows.Select(row => (object?)new Dictionary<string, object?>
                {
                    ["id"] = row.Id,
                    ["name"] = row.Name,
                    ["email"] = row.Email,
                    ["is_admin"] = row.IsAdmin,
                    ["is_agent"] = row.IsAgent,
                    ["roles"] = held.Where(h => h.UserId == row.Id).Select(h => h.Slug).Distinct()
                        .OrderBy(slug => slug, StringComparer.Ordinal).ToList(),
                }).ToList();
                roleOptions = (await _db.Roles.AsNoTracking()
                        .Where(r => !GateRoleSlugs.Contains(r.Slug))
                        .Select(r => new { r.Id, r.Name, r.Slug })
                        .ToListAsync(ct))
                    .OrderBy(r => r.Name, StringComparer.Ordinal).ThenBy(r => r.Id)
                    .Select(r => new Dictionary<string, object?> { ["id"] = r.Id, ["name"] = r.Name, ["slug"] = r.Slug })
                    .ToList();
            }

            return UiResults.Render("Escalated/Admin/Users/Index", new Dictionary<string, object?>
            {
                ["users"] = UiPresenter.Paginator(pageRows, total, safePage, perPage, Request.Path, query),
                ["filters"] = new Dictionary<string, object?> { ["search"] = term },
                ["currentUserId"] = this.CurrentUserId(),
                ["notice"] = null,
                // Null while the switch is off: the screen then shows no roles column.
                ["roles"] = roleOptions,
            });
        }

        return Ok(new IndexResponse(
            Users: new PaginatedUsers(rows, safePage, perPage, total, lastPage),
            Filters: new IndexFilters(term),
            CurrentUserId: this.CurrentUserId()));
    }

    /// <summary>Top-level shape consumed by <c>Escalated/Admin/Users/Index</c>.</summary>
    public record IndexResponse(
        [property: JsonPropertyName("users")] PaginatedUsers Users,
        [property: JsonPropertyName("filters")] IndexFilters Filters,
        [property: JsonPropertyName("currentUserId")] string? CurrentUserId);

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
    [HttpPatch("{userId}/role")]
    public async Task<IActionResult> UpdateRole(
        string userId,
        [FromBody] UpdateRoleRequest request,
        CancellationToken ct = default)
    {
        var currentUserId = this.CurrentUserId();

        if (request is null || (request.Role != "admin" && request.Role != "agent"))
        {
            return IsPage
                ? UiResults.Back(HttpContext, IndexUrl, errors: new Dictionary<string, string> { ["role"] = "The selected role is invalid." })
                : BadRequest(new { error = "Role must be 'admin' or 'agent'." });
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
            && !string.IsNullOrEmpty(currentUserId)
            && currentUserId == target.Id)
        {
            return IsPage
                ? UiResults.Back(HttpContext, IndexUrl, error: "You cannot remove your own admin role.")
                : BadRequest(new { error = "You cannot remove your own admin role." });
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

        return IsPage ? UiResults.Back(HttpContext, IndexUrl, success: "User updated.") : Ok(new { message = "User updated." });
    }

    /// <summary>
    /// PUT /support/admin/users/{userId}/roles: replaces the user's Escalated roles with
    /// <c>role_ids</c>, as escalated-laravel's <c>UserController::updateRoles</c>. Only while
    /// roles and permissions is on (404 otherwise, <see cref="EnsureAccessFeaturesFilter"/>);
    /// needs <c>role.manage</c> (<see cref="RoutePermissions"/>). The gate roles
    /// (<c>escalated-admin</c>, <c>escalated-agent</c>) are kept and cannot be given here.
    /// </summary>
    [HttpPut("{userId}/roles")]
    public async Task<IActionResult> UpdateRoles(string userId, [FromBody] JsonElement body, CancellationToken ct = default)
    {
        var (roleIds, errors) = await ValidateRoleIdsAsync(body, ct);
        if (errors is not null)
        {
            return IsPage
                ? UiResults.Back(HttpContext, IndexUrl, errors: errors)
                : UnprocessableEntity(new { message = errors.Values.First(), errors });
        }

        var target = await _directory.FindAsync(userId, ct);
        if (target is null)
        {
            return NotFound(new { error = "User not found." });
        }

        // Taking away your own role management would leave nobody able to undo it.
        var currentUserId = this.CurrentUserId();
        if (!string.IsNullOrEmpty(currentUserId) && currentUserId == target.Id
            && !await _db.RolePermissions.AnyAsync(rp => roleIds.Contains(rp.RoleId) && rp.Permission != null && rp.Permission.Slug == "role.manage", ct))
        {
            const string message = "You cannot remove your own role management.";
            return IsPage ? UiResults.Back(HttpContext, IndexUrl, error: message) : BadRequest(new { error = message });
        }

        var held = await _db.RoleUsers
            .Where(ru => ru.UserId == target.Id && ru.Role != null && !GateRoleSlugs.Contains(ru.Role.Slug))
            .ToListAsync(ct);
        _db.RoleUsers.RemoveRange(held);
        foreach (var roleId in roleIds)
        {
            _db.RoleUsers.Add(new RoleUser { UserId = target.Id, RoleId = roleId });
        }

        await _db.SaveChangesAsync(ct);

        return IsPage ? UiResults.Back(HttpContext, IndexUrl, success: "User updated.") : Ok(new { message = "User updated." });
    }

    /// <summary>
    /// Laravel's rules for <c>role_ids</c>: present, an array, of integers naming roles (here,
    /// never a gate role). Errors are keyed as Laravel keys them (<c>role_ids</c>, <c>role_ids.N</c>).
    /// </summary>
    private async Task<(List<int> Ids, Dictionary<string, string>? Errors)> ValidateRoleIdsAsync(JsonElement body, CancellationToken ct)
    {
        static (List<int>, Dictionary<string, string>?) Fail(string key, string message) =>
            (new List<int>(), new Dictionary<string, string> { [key] = message });

        if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("role_ids", out var raw) || raw.ValueKind == JsonValueKind.Null)
        {
            return Fail("role_ids", "The role ids field must be present.");
        }

        if (raw.ValueKind != JsonValueKind.Array)
        {
            return Fail("role_ids", "The role ids field must be an array.");
        }

        var ids = new List<int>();
        var index = 0;
        foreach (var item in raw.EnumerateArray())
        {
            int id;
            var ok = item.ValueKind == JsonValueKind.Number
                ? item.TryGetInt32(out id)
                : int.TryParse(item.ValueKind == JsonValueKind.String ? item.GetString() : null,
                    System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out id);
            if (!ok)
            {
                return Fail($"role_ids.{index}", $"The role_ids.{index} field must be an integer.");
            }

            ids.Add(id);
            index++;
        }

        var known = ids.Count == 0
            ? new List<int>()
            : await _db.Roles.AsNoTracking()
                .Where(r => ids.Contains(r.Id) && !GateRoleSlugs.Contains(r.Slug))
                .Select(r => r.Id)
                .ToListAsync(ct);
        var unknown = ids.FindIndex(id => !known.Contains(id));
        if (unknown >= 0)
        {
            return Fail($"role_ids.{unknown}", $"The selected role_ids.{unknown} is invalid.");
        }

        return (ids.Distinct().ToList(), null);
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

    private Task<bool> HasRoleAsync(string userId, int roleId, CancellationToken ct)
        => _db.RoleUsers.AnyAsync(ru => ru.UserId == userId && ru.RoleId == roleId, ct);

    private async Task SetRoleAsync(string userId, int roleId, bool value, CancellationToken ct)
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

    private record RoleAssignment(string UserId, string Slug);

    /// <summary>Row shape returned to the shared Vue page.</summary>
    public record UserRow(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("email")] string? Email,
        [property: JsonPropertyName("is_admin")] bool IsAdmin,
        [property: JsonPropertyName("is_agent")] bool IsAgent);
}
