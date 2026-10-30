using Escalated.Data;
using Escalated.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Escalated.Authorization;

/// <summary>
/// escalated-laravel's permission set and its system roles (<c>admin</c>, <c>agent</c>,
/// <c>light_agent</c>), as its <c>PermissionSeeder</c>: what roles &amp; permissions enforces
/// (ADR 2026-10-29-configurable-access-features). Idempotent: missing permissions and roles are
/// created and a system role gains the permissions it lacks; nothing is removed.
/// <see cref="PermissionSeederHostedService"/> runs it at startup while the switch is on;
/// <see cref="AssignRoleAsync"/> bootstraps the first admin (Laravel's <c>escalated:roles:assign</c>).
/// </summary>
public sealed class PermissionSeeder
{
    /// <summary>(slug, name, group).</summary>
    public static readonly IReadOnlyList<(string Slug, string Name, string Group)> Permissions = new[]
    {
        ("ticket.view", "View tickets", "Tickets"),
        ("ticket.create", "Create tickets", "Tickets"),
        ("ticket.edit", "Edit ticket properties", "Tickets"),
        ("ticket.delete", "Delete tickets", "Tickets"),
        ("ticket.assign", "Assign tickets", "Tickets"),
        ("ticket.merge", "Merge tickets", "Tickets"),
        ("ticket.close", "Close tickets", "Tickets"),
        ("ticket.export", "Export tickets", "Tickets"),
        ("reply.create", "Reply to tickets", "Replies"),
        ("reply.create_internal", "Add internal notes", "Replies"),
        ("reply.edit", "Edit replies", "Replies"),
        ("reply.delete", "Delete replies", "Replies"),
        ("kb.view", "View knowledge base", "Knowledge Base"),
        ("kb.create", "Create articles", "Knowledge Base"),
        ("kb.edit", "Edit articles", "Knowledge Base"),
        ("kb.delete", "Delete articles", "Knowledge Base"),
        ("kb.publish", "Publish articles", "Knowledge Base"),
        ("department.view", "View departments", "Departments"),
        ("department.create", "Create departments", "Departments"),
        ("department.edit", "Edit departments", "Departments"),
        ("department.delete", "Delete departments", "Departments"),
        ("report.view", "View reports", "Reports"),
        ("report.export", "Export reports", "Reports"),
        ("sla.view", "View SLA policies", "SLA"),
        ("sla.manage", "Manage SLA policies", "SLA"),
        ("automation.view", "View automations", "Automations"),
        ("automation.manage", "Manage automations", "Automations"),
        ("escalation.view", "View escalation rules", "Escalation Rules"),
        ("escalation.manage", "Manage escalation rules", "Escalation Rules"),
        ("macro.view", "View macros", "Macros"),
        ("macro.create", "Create macros", "Macros"),
        ("macro.manage", "Manage macros", "Macros"),
        ("tag.view", "View tags", "Tags"),
        ("tag.manage", "Manage tags", "Tags"),
        ("custom_field.view", "View custom fields", "Custom Fields"),
        ("custom_field.manage", "Manage custom fields", "Custom Fields"),
        ("role.view", "View roles", "Roles"),
        ("role.manage", "Manage roles", "Roles"),
        ("user.view", "View users", "Users"),
        ("user.manage", "Manage users", "Users"),
        ("settings.view", "View settings", "Settings"),
        ("settings.manage", "Manage settings", "Settings"),
        ("webhook.view", "View webhooks", "Webhooks"),
        ("webhook.manage", "Manage webhooks", "Webhooks"),
        ("api_token.view", "View API tokens", "API Tokens"),
        ("api_token.manage", "Manage API tokens", "API Tokens"),
        ("audit.view", "View audit log", "Audit Log"),
        ("plugin.view", "View plugins", "Plugins"),
        ("plugin.manage", "Manage plugins", "Plugins"),
        ("custom_object.view", "View custom objects", "Custom Objects"),
        ("custom_object.manage", "Manage custom objects", "Custom Objects"),
        ("custom_object.data", "Manage custom object data", "Custom Objects"),
        ("newsletters.manage", "Manage newsletters", "Newsletters"),
        ("newsletters.send", "Send newsletters", "Newsletters"),
    };

    /// <summary>The system roles: slug, name, permission patterns (<c>*</c> is every permission).</summary>
    public static readonly IReadOnlyList<(string Slug, string Name, string Description, string[] Patterns)> Roles = new[]
    {
        ("admin", "Admin", "Full access to all features and settings.", new[] { "*" }),
        ("agent", "Agent", "Standard agent with ticket handling and limited administrative access.", new[]
        {
            "ticket.*", "reply.*", "kb.view", "report.view", "macro.view", "macro.create", "tag.view",
            "custom_field.view", "audit.view",
        }),
        ("light_agent", "Light Agent", "Limited agent with read-only ticket access and internal note capability.", new[]
        {
            "ticket.view", "reply.create", "reply.create_internal", "kb.view", "macro.view", "tag.view",
        }),
    };

    private readonly EscalatedDbContext _db;

    public PermissionSeeder(EscalatedDbContext db)
    {
        _db = db;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        foreach (var (slug, name, group) in Permissions)
        {
            if (!await _db.Permissions.AnyAsync(p => p.Slug == slug, ct))
            {
                _db.Permissions.Add(new Permission { Slug = slug, Name = name, Group = group });
            }
        }

        await _db.SaveChangesAsync(ct);
        var all = await _db.Permissions.AsNoTracking().ToListAsync(ct);

        foreach (var (slug, name, description, patterns) in Roles)
        {
            var role = await _db.Roles.FirstOrDefaultAsync(r => r.Slug == slug, ct);
            if (role is null)
            {
                role = new Role { Slug = slug, Name = name, Description = description, IsSystem = true };
                _db.Roles.Add(role);
                await _db.SaveChangesAsync(ct);
            }

            var held = await _db.RolePermissions.Where(rp => rp.RoleId == role.Id).Select(rp => rp.PermissionId).ToListAsync(ct);
            foreach (var permission in all.Where(p => Matches(patterns, p.Slug) && !held.Contains(p.Id)))
            {
                _db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id });
            }

            await _db.SaveChangesAsync(ct);
        }
    }

    /// <summary>Gives <paramref name="userId"/> the role <paramref name="roleSlug"/> (kept with any others).</summary>
    public async Task AssignRoleAsync(string userId, string roleSlug, CancellationToken ct = default)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Slug == roleSlug, ct)
            ?? throw new InvalidOperationException($"No role with slug {roleSlug}.");
        if (!await _db.RoleUsers.AnyAsync(ru => ru.RoleId == role.Id && ru.UserId == userId, ct))
        {
            _db.RoleUsers.Add(new RoleUser { RoleId = role.Id, UserId = userId });
            await _db.SaveChangesAsync(ct);
        }
    }

    internal static bool Matches(IEnumerable<string> patterns, string slug) => patterns.Any(pattern =>
        pattern == "*"
        || (pattern.EndsWith(".*", StringComparison.Ordinal) && slug.StartsWith(pattern[..^1], StringComparison.Ordinal))
        || pattern == slug);
}

/// <summary>Seeds <see cref="PermissionSeeder"/> at startup while roles &amp; permissions is on.</summary>
public sealed class PermissionSeederHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<PermissionSeederHostedService> _logger;

    public PermissionSeederHostedService(IServiceScopeFactory scopes, ILogger<PermissionSeederHostedService> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopes.CreateScope();
        if (!scope.ServiceProvider.GetRequiredService<AccessFeatures>().RolesPermissions)
        {
            return;
        }

        try
        {
            await scope.ServiceProvider.GetRequiredService<PermissionSeeder>().SeedAsync(cancellationToken);
        }
        catch (Exception failed)
        {
            _logger.LogWarning(failed, "Escalated could not seed its permissions and system roles.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
