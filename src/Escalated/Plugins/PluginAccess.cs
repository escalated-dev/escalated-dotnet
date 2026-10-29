using System.Security.Claims;
using Escalated.Authorization;
using Escalated.Configuration;
using Escalated.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Escalated.Plugins;

/// <summary>
/// The user a plugin call acts for: the signed-in user of the request it serves,
/// or <see cref="None"/> outside a request (event delivery). A plugin never names
/// the actor itself.
/// </summary>
public sealed record PluginActor(string? UserId, ClaimsPrincipal? User)
{
    public static readonly PluginActor None = new(null, null);
}

/// <summary>
/// Who may see a plugin page, endpoint, menu item, slot component or widget that
/// declares a <c>capability</c>, and who is an admin.
///
/// <para>A capability is an ASP.NET Core authorization policy when the host has
/// registered one by that name, and otherwise an Escalated permission slug the
/// user holds through their roles (<c>escalated_role_permission</c>).</para>
/// </summary>
public sealed class PluginAccess
{
    private readonly IAuthorizationService _authorization;
    private readonly IAuthorizationPolicyProvider _policies;
    private readonly EscalatedDbContext _db;
    private readonly EscalatedOptions _options;

    public PluginAccess(IAuthorizationService authorization, IAuthorizationPolicyProvider policies, EscalatedDbContext db,
        IOptions<EscalatedOptions> options)
    {
        _authorization = authorization;
        _policies = policies;
        _db = db;
        _options = options.Value;
    }

    public async Task<bool> IsAdminAsync(ClaimsPrincipal? user) =>
        user is not null && (await _authorization.AuthorizeAsync(user, EscalatedPolicies.Admin)).Succeeded;

    /// <summary>Whether <paramref name="user"/> holds <paramref name="capability"/>. No capability: yes.</summary>
    public async Task<bool> AllowsAsync(ClaimsPrincipal? user, string? capability, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(capability)) return true;
        if (user is null) return false;

        if (await _policies.GetPolicyAsync(capability) is not null)
        {
            return (await _authorization.AuthorizeAsync(user, capability)).Succeeded;
        }

        var userId = EscalatedUser.ResolveId(user, _options);
        if (userId is null) return false;

        return await _db.RoleUsers.AsNoTracking()
            .Where(ru => ru.UserId == userId)
            .Join(_db.RolePermissions.AsNoTracking(), ru => ru.RoleId, rp => rp.RoleId, (ru, rp) => rp)
            .AnyAsync(rp => rp.Permission != null && rp.Permission.Slug == capability, ct);
    }
}
