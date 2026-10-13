using System.Security.Claims;
using Escalated.Configuration;
using Escalated.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Escalated.Authorization;

public record TicketAccount(string Type, string Id);
public record TicketAccountReference(string Kind, string Type, string Id, object? Model = null);
public sealed record TicketAccessActor(string UserId, bool DepartmentRestricted, bool AccountRestricted, TicketAccount? Account);

/// <summary>Async request scope shared by package contexts. An absent actor is trusted background work.</summary>
public static class TicketAccessScope
{
    private static readonly AsyncLocal<TicketAccessActor?> Actor = new();
    public static TicketAccessActor? Current => Actor.Value;

    public static async Task<T> RunAsync<T>(TicketAccessActor? actor, Func<Task<T>> work)
    {
        var previous = Actor.Value;
        Actor.Value = actor;
        try { return await work(); }
        finally { Actor.Value = previous; }
    }

    public static Task<T> WithoutVisibilityAsync<T>(Func<Task<T>> work) => RunAsync(null, work);
}

public sealed class TicketAccessDeniedException : Exception
{
    public TicketAccessDeniedException() : base("Ticket not found.") { }
}

/// <summary>Uses the host's existing authorization policies and optional account resolver.</summary>
public sealed class TicketAccess
{
    private readonly EscalatedOptions options;
    private readonly IAuthorizationService authorization;
    private readonly EscalatedDbContext db;

    public TicketAccess(IOptions<EscalatedOptions> options, IAuthorizationService authorization, EscalatedDbContext db)
    {
        this.options = options.Value;
        this.authorization = authorization;
        this.db = db;
    }

    public string? UserId(ClaimsPrincipal? user) => EscalatedUser.ResolveId(user, options);

    public bool IsRestricted => options.Visibility == "department" || options.Accounts.ScopeAgents;

    public async Task<ClaimsPrincipal> PrincipalAsync(string userId)
    {
        if (options.TicketAccessUserResolver is not null)
            return await options.TicketAccessUserResolver(userId) ?? new ClaimsPrincipal();
        return new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId) }, "Escalated"));
    }

    public async Task<bool> IsAgentAsync(ClaimsPrincipal user) =>
        (await authorization.AuthorizeAsync(user, null, EscalatedPolicies.Agent)).Succeeded;

    public async Task<bool> IsAdminAsync(ClaimsPrincipal user) =>
        (await authorization.AuthorizeAsync(user, null, EscalatedPolicies.Admin)).Succeeded;

    public async Task<T> ForUserAsync<T>(ClaimsPrincipal user, Func<Task<T>> work, CancellationToken ct = default)
    {
        var id = EscalatedUser.ResolveId(user, options);
        if (!IsRestricted || id is null || await IsAdminAsync(user) || !await IsAgentAsync(user))
            return await TicketAccessScope.RunAsync(null, work);
        var account = options.Accounts.ScopeAgents
            ? await ResolveAccountAsync(new TicketAccountReference("agent", "User", id, user), ct) : null;
        return await TicketAccessScope.RunAsync(new TicketAccessActor(id, options.Visibility == "department", options.Accounts.ScopeAgents, account), work);
    }

    public Task<T> ForUserIdAsync<T>(string userId, Func<Task<T>> work, CancellationToken ct = default) =>
        ForResolvedUserAsync(userId, work, ct);

    private async Task<T> ForResolvedUserAsync<T>(string userId, Func<Task<T>> work, CancellationToken ct)
    {
        var principal = await PrincipalAsync(userId);
        return await ForUserAsync(principal, work, ct);
    }

    public async Task<TicketAccount?> ResolveAccountAsync(TicketAccountReference reference, CancellationToken ct = default)
    {
        if (!options.Accounts.Enabled || options.Accounts.Resolver is null) return null;
        var account = await options.Accounts.Resolver(reference, ct);
        return account is { Type.Length: > 0 and <= 191, Id.Length: > 0 and <= 191 } ? account : null;
    }

    public async Task<bool> CanViewAsync(ClaimsPrincipal user, int ticketId, CancellationToken ct = default)
    {
        var id = EscalatedUser.ResolveId(user, options);
        if (id is null) return false;
        var staff = await IsAdminAsync(user) || await IsAgentAsync(user);
        return await ForUserAsync(user, () => db.Tickets.AsNoTracking().AnyAsync(t => t.Id == ticketId && (staff || t.RequesterId == id), ct), ct);
    }

    public async Task<bool> AgentCanViewAsync(string userId, int ticketId, CancellationToken ct = default)
    {
        if (!IsRestricted) return true;
        var user = await PrincipalAsync(userId);
        if (!await IsAdminAsync(user) && !await IsAgentAsync(user)) return false;
        return await CanViewAsync(user, ticketId, ct);
    }
}
