using Escalated.Data;
using Escalated.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Services.Newsletter;

public class NewsletterPermissionService
{
    private readonly EscalatedDbContext _db;

    public NewsletterPermissionService(EscalatedDbContext db)
    {
        _db = db;
    }

    public async Task<bool> HasAsync(HttpContext httpContext, string permission, CancellationToken ct = default)
    {
        var apiToken = httpContext.Items["EscalatedApiToken"] as ApiToken;
        if (apiToken is not null && !apiToken.HasAbility(permission))
            return false;

        var userId = httpContext.User?.FindFirst("sub")?.Value
            ?? httpContext.User?.FindFirst("id")?.Value
            ?? httpContext.User?.Identity?.Name;

        if (string.IsNullOrWhiteSpace(userId))
            return false;

        var roles = await _db.RoleUsers
            .Where(ru => ru.UserId == userId)
            .Select(ru => ru.RoleId)
            .ToListAsync(ct);

        if (roles.Count == 0)
            return false;

        if (await _db.Roles.AnyAsync(r => roles.Contains(r.Id) && r.Slug == "admin", ct))
            return true;

        return await _db.RolePermissions
            .Include(rp => rp.Permission)
            .AnyAsync(rp => roles.Contains(rp.RoleId) && rp.Permission != null && rp.Permission.Slug == permission, ct);
    }

    public async Task RequireAsync(HttpContext httpContext, string permission, CancellationToken ct = default)
    {
        if (!await HasAsync(httpContext, permission, ct))
            throw new UnauthorizedAccessException("Insufficient permissions.");
    }
}

public class NewsletterPermissionSeeder
{
    private readonly EscalatedDbContext _db;

    public NewsletterPermissionSeeder(EscalatedDbContext db)
    {
        _db = db;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        var manage = await UpsertPermissionAsync(
            "newsletters.manage",
            "Manage newsletters",
            "Newsletters",
            ct);
        var send = await UpsertPermissionAsync(
            "newsletters.send",
            "Send newsletters",
            "Newsletters",
            ct);

        await _db.SaveChangesAsync(ct);

        var admin = await _db.Roles.SingleOrDefaultAsync(r => r.Slug == "admin", ct);
        if (admin is not null)
        {
            await AttachAsync(admin.Id, manage.Id, ct);
            await AttachAsync(admin.Id, send.Id, ct);
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task<Permission> UpsertPermissionAsync(string slug, string name, string group, CancellationToken ct)
    {
        var permission = await _db.Permissions.SingleOrDefaultAsync(p => p.Slug == slug, ct);
        if (permission is null)
        {
            permission = new Permission { Slug = slug };
            _db.Permissions.Add(permission);
        }

        permission.Name = name;
        permission.Group = group;
        return permission;
    }

    private async Task AttachAsync(int roleId, int permissionId, CancellationToken ct)
    {
        if (!await _db.RolePermissions.AnyAsync(rp => rp.RoleId == roleId && rp.PermissionId == permissionId, ct))
            _db.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = permissionId });
    }
}
