using Escalated.Data;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Authorization;

/// <summary>
/// What a user may do under roles &amp; permissions (ADR 2026-10-29-configurable-access-features):
/// the union of the permissions of the roles they hold (<c>RoleUsers</c>, <c>RolePermissions</c>).
/// Nothing else grants a permission. The gate roles <c>escalated-admin</c> and
/// <c>escalated-agent</c> decide who reaches the admin and agent areas and hold no permissions
/// of their own, so passing the admin gate grants nothing; a user who should have everything
/// holds the <c>admin</c> role.
/// </summary>
public sealed class AccessControl
{
    private readonly AccessFeatures _features;
    private readonly EscalatedDbContext _db;

    public AccessControl(AccessFeatures features, EscalatedDbContext db)
    {
        _features = features;
        _db = db;
    }

    public bool Enforced => _features.RolesPermissions;

    /// <summary>The permission slugs <paramref name="userId"/>'s roles grant.</summary>
    public async Task<HashSet<string>> PermissionsOfAsync(string? userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return new HashSet<string>();
        }

        var roleIds = _db.RoleUsers.AsNoTracking().Where(ru => ru.UserId == userId).Select(ru => ru.RoleId);
        var slugs = await _db.RolePermissions.AsNoTracking()
            .Where(rp => roleIds.Contains(rp.RoleId) && rp.Permission != null)
            .Select(rp => rp.Permission!.Slug)
            .Distinct()
            .ToListAsync(ct);
        return new HashSet<string>(slugs, StringComparer.Ordinal);
    }

    /// <summary>Whether the user holds every one of <paramref name="slugs"/>. Always true while the switch is off.</summary>
    public async Task<bool> AllowsAsync(string? userId, IEnumerable<string> slugs, CancellationToken ct = default)
    {
        if (!Enforced)
        {
            return true;
        }

        var wanted = slugs.ToList();
        if (wanted.Count == 0)
        {
            return true;
        }

        var held = await PermissionsOfAsync(userId, ct);
        return wanted.All(held.Contains);
    }

    /// <summary>
    /// The <c>escalated.permissions</c> shared prop: the user's slugs while the switch is on;
    /// while it is off, <c>['*']</c> for staff (the gates decide everything) and nothing for anyone else.
    /// </summary>
    public async Task<List<string>> SharedAsync(string? userId, bool staff, CancellationToken ct = default)
    {
        if (!Enforced)
        {
            return staff ? new List<string> { "*" } : new List<string>();
        }

        return (await PermissionsOfAsync(userId, ct)).OrderBy(slug => slug, StringComparer.Ordinal).ToList();
    }
}
