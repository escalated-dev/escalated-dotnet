using System.Globalization;
using System.Text.Json;
using Escalated.Middleware;
using Escalated.Models;
using Escalated.Ui;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Controllers.Ui;

/// <summary>
/// The roles and API token pages, with escalated-laravel's routes and props:
/// <c>Roles/Index</c> and <c>Form</c> (<c>RoleController</c>) and
/// <c>ApiTokens/Index</c> (<c>ApiTokenController</c>). <c>Users/Index</c> is served by
/// the users admin API itself (<see cref="Admin.AdminUsersController"/>).
/// </summary>
public partial class AdminPageController
{
    private static readonly HashSet<string> TokenAbilities = new() { "agent", "admin", "*" };

    // --- Roles ---

    [HttpGet("roles")]
    [EscalatedPage(Negotiate = true)]
    public async Task<IActionResult> Roles(CancellationToken ct)
    {
        var roles = await _db.Roles.AsNoTracking().OrderBy(r => r.Name).ThenBy(r => r.Id)
            .Select(r => new { Role = r, Users = _db.RoleUsers.Count(ru => ru.RoleId == r.Id) })
            .ToListAsync(ct);

        return UiResults.Render("Escalated/Admin/Roles/Index", new
        {
            roles = roles.Select(row =>
            {
                var item = Role(row.Role);
                item["users_count"] = row.Users;
                return item;
            }).ToList(),
        });
    }

    [HttpGet("roles/create")]
    public async Task<IActionResult> CreateRole(CancellationToken ct) =>
        UiResults.Render("Escalated/Admin/Roles/Form", new Dictionary<string, object?>
        {
            ["role"] = null,
            ["permissions"] = await PermissionGroupsAsync(ct),
        });

    [HttpGet("roles/{id:int}/edit")]
    public async Task<IActionResult> EditRole(int id, CancellationToken ct)
    {
        var role = await _db.Roles.AsNoTracking().Include(r => r.Permissions).FirstOrDefaultAsync(r => r.Id == id, ct);
        if (role is null) return NotFound();

        var item = Role(role);
        item["permissions"] = role.Permissions.OrderBy(p => p.Id).Select(Permission).ToList();
        return UiResults.Render("Escalated/Admin/Roles/Form", new { role = item, permissions = await PermissionGroupsAsync(ct) });
    }

    private static Dictionary<string, object?> Role(Role role) => new()
    {
        ["id"] = role.Id,
        ["name"] = role.Name,
        ["slug"] = role.Slug,
        ["description"] = role.Description,
        ["is_system"] = role.IsSystem,
        ["created_at"] = role.CreatedAt,
        ["updated_at"] = role.UpdatedAt,
    };

    private static Dictionary<string, object?> Permission(Permission permission) => new()
    {
        ["id"] = permission.Id,
        ["name"] = permission.Name,
        ["slug"] = permission.Slug,
        ["group"] = permission.Group,
    };

    /// <summary>Every permission grouped by its group, as Laravel's <c>Permission::all()->groupBy('group')</c>.</summary>
    private async Task<Dictionary<string, object?>> PermissionGroupsAsync(CancellationToken ct)
    {
        var permissions = await _db.Permissions.AsNoTracking().OrderBy(p => p.Id).ToListAsync(ct);
        return permissions.GroupBy(p => p.Group ?? string.Empty)
            .ToDictionary(g => g.Key, g => (object?)g.Select(Permission).ToList());
    }

    [HttpPost("roles")]
    [EscalatedPage(Negotiate = true)]
    public Task<IActionResult> StoreRole(CancellationToken ct) => SaveRoleAsync(null, ct);

    [HttpPut("roles/{id:int}")]
    public Task<IActionResult> UpdateRole(int id, CancellationToken ct) => SaveRoleAsync(id, ct);

    private async Task<IActionResult> SaveRoleAsync(int? id, CancellationToken ct)
    {
        var role = id is null ? null : await _db.Roles.Include(r => r.Permissions).FirstOrDefaultAsync(r => r.Id == id, ct);
        if (id is not null && role is null) return NotFound();

        var input = await UiInput.ReadAsync(Request, ct);
        var errors = Errors();
        var name = input.String("name")?.Trim();
        if (name is null) errors["name"] = "The name field is required.";
        else if (name.Length > 100) errors["name"] = "The name may not be greater than 100 characters.";
        else
        {
            var lowered = name.ToLowerInvariant();
            if (await _db.Roles.AnyAsync(r => r.Name.ToLower() == lowered && r.Id != (id ?? 0), ct))
            {
                errors["name"] = "A role with this name already exists.";
            }
        }

        var ids = input.IntList("permissions");
        var permissions = new List<Permission>();
        if (ids is null) errors["permissions"] = "The permissions must be integers.";
        else if (ids.Count > 0)
        {
            permissions = await _db.Permissions.Where(p => ids.Contains(p.Id)).ToListAsync(ct);
            if (permissions.Count != ids.Distinct().Count()) errors["permissions"] = "The selected permissions are invalid.";
        }

        var formUrl = id is null ? $"{Prefix}/admin/roles/create" : $"{Prefix}/admin/roles/{id}/edit";
        if (errors.Count > 0) return UiResults.Back(HttpContext, formUrl, errors: errors);

        var now = DateTime.UtcNow;
        if (role is null)
        {
            // A distinct name can still slug onto an existing role; suffix until it is free.
            var baseSlug = RoleSlug.From(name!);
            var slug = baseSlug;
            for (var n = 2; await _db.Roles.AnyAsync(r => r.Slug == slug, ct); n++)
            {
                slug = $"{baseSlug[..Math.Min(baseSlug.Length, 90)]}-{n}";
            }

            role = new Role { Slug = slug, CreatedAt = now };
            _db.Roles.Add(role);
        }

        role.Name = name!;
        role.Description = input.String("description");
        role.UpdatedAt = now;
        role.Permissions.Clear();
        foreach (var permission in permissions)
        {
            role.Permissions.Add(permission);
        }

        await _db.SaveChangesAsync(ct);
        return new UiRedirectResult($"{Prefix}/admin/roles") { Success = id is null ? "Role created." : "Role updated." };
    }

    [HttpDelete("roles/{id:int}")]
    public async Task<IActionResult> DestroyRole(int id, CancellationToken ct)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (role is null) return NotFound();

        var index = $"{Prefix}/admin/roles";
        if (role.IsSystem) return UiResults.Back(HttpContext, index, error: "System roles cannot be deleted.");

        await _db.RoleUsers.Where(ru => ru.RoleId == id).ExecuteDeleteAsync(ct);
        await _db.RolePermissions.Where(rp => rp.RoleId == id).ExecuteDeleteAsync(ct);
        _db.Roles.Remove(role);
        await _db.SaveChangesAsync(ct);
        return new UiRedirectResult(index) { Success = "Role deleted." };
    }

    // --- API tokens ---

    [HttpGet("api-tokens")]
    [EscalatedPage(Negotiate = true)]
    public async Task<IActionResult> ApiTokens([FromServices] ApiTokenAuthState api, CancellationToken ct)
    {
        var tokens = await _db.ApiTokens.AsNoTracking().OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id).ToListAsync(ct);
        var rows = new List<Dictionary<string, object?>>();
        foreach (var token in tokens)
        {
            var owner = await _present.UserAsync(token.TokenableId?.ToString(CultureInfo.InvariantCulture), ct);
            rows.Add(new Dictionary<string, object?>
            {
                ["id"] = token.Id,
                ["name"] = token.Name,
                ["user_name"] = owner?["name"],
                ["user_email"] = owner?["email"],
                ["abilities"] = UiPresenter.ParseJson(token.Abilities) ?? Array.Empty<string>(),
                ["last_used_at"] = token.LastUsedAt,
                ["last_used_ip"] = null,
                ["expires_at"] = token.ExpiresAt,
                ["is_expired"] = token.IsExpired,
                ["created_at"] = token.CreatedAt,
            });
        }

        return UiResults.Render("Escalated/Admin/ApiTokens/Index", new
        {
            tokens = rows,
            users = await _present.AgentOptionsAsync(ct),
            api_enabled = api.Wired,
        });
    }

    /// <summary>
    /// Creates a token for a staff user. The plain token is shown once, as the
    /// <c>plain_text_token</c> flash the page reads; only its SHA-256 is stored.
    /// </summary>
    [HttpPost("api-tokens")]
    [EscalatedPage(Negotiate = true)]
    public async Task<IActionResult> StoreApiToken(CancellationToken ct)
    {
        var input = await UiInput.ReadAsync(Request, ct);
        var errors = Errors();
        var name = input.String("name");
        if (name is null) errors["name"] = "The name field is required.";
        else if (name.Length > 255) errors["name"] = "The name may not be greater than 255 characters.";
        var userId = input.Int("user_id");
        if (userId is null) errors["user_id"] = "The user id must be an integer.";
        else if (!(await _present.AgentOptionsAsync(ct)).Any(a => a["id"] as string == userId.Value.ToString(CultureInfo.InvariantCulture)))
        {
            errors["user_id"] = "The selected user is invalid.";
        }

        var abilities = input.List("abilities");
        if (abilities.Count == 0) errors["abilities"] = "The abilities field is required.";
        else if (abilities.Any(a => !TokenAbilities.Contains(a))) errors["abilities"] = "The selected abilities are invalid.";
        var days = input.Int("expires_in_days");
        if (input.String("expires_in_days") is not null && days is not (>= 1 and <= 365))
        {
            errors["expires_in_days"] = "The expires in days must be between 1 and 365.";
        }

        var back = $"{Prefix}/admin/api-tokens";
        if (errors.Count > 0) return UiResults.Back(HttpContext, back, errors: errors);

        var plain = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        _db.ApiTokens.Add(new ApiToken
        {
            Name = name!,
            TokenHash = ApiTokenAuthMiddleware.ComputeSha256(plain),
            TokenableType = "User",
            TokenableId = userId,
            Abilities = JsonSerializer.Serialize(abilities.Distinct().ToList()),
            ExpiresAt = days is { } d ? now.AddDays(d) : null,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await _db.SaveChangesAsync(ct);

        UiFlash.Put(HttpContext, "plain_text_token", plain);
        return UiResults.Back(HttpContext, back, success: "API token created.");
    }

    [HttpDelete("api-tokens/{id:int}")]
    [EscalatedPage(Negotiate = true)]
    public async Task<IActionResult> DestroyApiToken(int id, CancellationToken ct)
    {
        var token = await _db.ApiTokens.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (token is null) return NotFound();

        _db.ApiTokens.Remove(token);
        await _db.SaveChangesAsync(ct);
        return UiResults.Back(HttpContext, $"{Prefix}/admin/api-tokens", success: "Token revoked.");
    }
}
