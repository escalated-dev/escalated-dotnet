using System.Security.Claims;
using Escalated.Configuration;
using Escalated.Controllers.Admin;
using Escalated.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Escalated.Authorization;

/// <summary>
/// The authorization policies Escalated's controllers require. Named after the
/// Laravel reference's <c>escalated-admin</c> and <c>escalated-agent</c> gates.
///
/// <para>Escalated owns no authentication: it reads the user the host signed in
/// from <c>HttpContext.User</c>. The defaults below deny anyone that host
/// authentication did not identify, and grant the staff surfaces through the
/// <c>escalated-admin</c> and <c>escalated-agent</c> roles that the admin users
/// page manages. A host replaces any of them by registering a policy with the
/// same name, before or after <c>AddEscalated</c>:</para>
/// <code>
/// builder.Services.AddAuthorization(o =>
///     o.AddPolicy(EscalatedPolicies.Admin, p => p.RequireRole("SupportLead")));
/// </code>
/// </summary>
public static class EscalatedPolicies
{
    /// <summary>The admin panel: <c>/support/admin/*</c> and <c>/admin/newsletters*</c>.</summary>
    public const string Admin = "escalated-admin";

    /// <summary>The agent workspace: <c>/support/agent/*</c>. Admins are agents.</summary>
    public const string Agent = "escalated-agent";

    /// <summary>The customer portal: <c>/support/tickets*</c> and attachment downloads.</summary>
    public const string Customer = "escalated-customer";
}

/// <summary>Resolves the host user id Escalated acts as.</summary>
public static class EscalatedUser
{
    /// <summary>
    /// The default resolver: the <see cref="ClaimTypes.NameIdentifier"/> claim, then
    /// <c>sub</c>, then <c>id</c>. ASP.NET Core Identity and JWT bearer authentication
    /// both put the user id in the first or second. An unauthenticated principal has
    /// no id, whatever claims it carries.
    /// </summary>
    public static string? DefaultResolveId(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        return NonEmpty(user.FindFirst(ClaimTypes.NameIdentifier)?.Value)
            ?? NonEmpty(user.FindFirst("sub")?.Value)
            ?? NonEmpty(user.FindFirst("id")?.Value);
    }

    /// <summary>The user id for <paramref name="user"/>, through the configured resolver.</summary>
    public static string? ResolveId(ClaimsPrincipal? user, EscalatedOptions? options)
    {
        if (user is null)
        {
            return null;
        }

        return NonEmpty((options?.UserIdResolver ?? DefaultResolveId)(user));
    }

    /// <summary>
    /// The signed-in user's id, for a controller action. Never read an acting user's
    /// id from the query string or the body: the caller controls both.
    /// </summary>
    internal static string? CurrentUserId(this ControllerBase controller)
    {
        var context = controller.HttpContext;
        var options = context?.RequestServices?.GetService<IOptions<EscalatedOptions>>()?.Value;

        return ResolveId(context?.User, options);
    }

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

/// <summary>Met when the configured resolver finds a user id on the principal.</summary>
public sealed class EscalatedUserRequirement : IAuthorizationRequirement
{
}

/// <summary>Met when the user holds any of <see cref="RoleSlugs"/> in Escalated's role table.</summary>
public sealed class EscalatedRoleRequirement : IAuthorizationRequirement
{
    public EscalatedRoleRequirement(params string[] roleSlugs)
    {
        RoleSlugs = roleSlugs;
    }

    public IReadOnlyList<string> RoleSlugs { get; }
}

public sealed class EscalatedUserHandler : AuthorizationHandler<EscalatedUserRequirement>
{
    private readonly IOptions<EscalatedOptions> _options;

    public EscalatedUserHandler(IOptions<EscalatedOptions> options)
    {
        _options = options;
    }

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, EscalatedUserRequirement requirement)
    {
        if (EscalatedUser.ResolveId(context.User, _options.Value) is not null)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Checks <see cref="EscalatedRoleRequirement"/> against <c>RoleUsers</c>, the table
/// <see cref="AdminUsersController"/> writes. Scoped, because it reads the database.
/// </summary>
public sealed class EscalatedRoleHandler : AuthorizationHandler<EscalatedRoleRequirement>
{
    private readonly EscalatedDbContext _db;
    private readonly IOptions<EscalatedOptions> _options;

    public EscalatedRoleHandler(EscalatedDbContext db, IOptions<EscalatedOptions> options)
    {
        _db = db;
        _options = options;
    }

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, EscalatedRoleRequirement requirement)
    {
        var userId = EscalatedUser.ResolveId(context.User, _options.Value);
        if (userId is null)
        {
            return;
        }

        var slugs = requirement.RoleSlugs.ToList();
        var holdsRole = await _db.RoleUsers
            .AnyAsync(ru => ru.UserId == userId && ru.Role != null && slugs.Contains(ru.Role.Slug));

        if (holdsRole)
        {
            context.Succeed(requirement);
        }
    }
}

internal static class EscalatedAuthorizationServiceCollectionExtensions
{
    internal static IServiceCollection AddEscalatedAuthorization(this IServiceCollection services)
    {
        // Core only: the full AddAuthorization() also registers the endpoint policy
        // cache, which needs routing. The host's AddControllers() brings that in.
        services.AddAuthorizationCore();
        services.AddSingleton<IAuthorizationHandler, EscalatedUserHandler>();
        services.AddScoped<IAuthorizationHandler, EscalatedRoleHandler>();

        // Only where the host has not defined the policy. Configure actions run in
        // registration order, so a host policy registered before AddEscalated is
        // found here and kept, and one registered after replaces this default.
        services.Configure<AuthorizationOptions>(options =>
        {
            AddIfMissing(options, EscalatedPolicies.Admin, policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(
                    new EscalatedUserRequirement(),
                    new EscalatedRoleRequirement(AdminUsersController.AdminRoleSlug)));

            AddIfMissing(options, EscalatedPolicies.Agent, policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(
                    new EscalatedUserRequirement(),
                    new EscalatedRoleRequirement(AdminUsersController.AgentRoleSlug, AdminUsersController.AdminRoleSlug)));

            AddIfMissing(options, EscalatedPolicies.Customer, policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(new EscalatedUserRequirement()));
        });

        return services;
    }

    private static void AddIfMissing(AuthorizationOptions options, string name, Action<AuthorizationPolicyBuilder> configure)
    {
        if (options.GetPolicy(name) is null)
        {
            options.AddPolicy(name, configure);
        }
    }
}
