using Escalated.Authorization;
using Escalated.Controllers.Admin;
using Escalated.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Escalated.Services;

/// <summary>
/// Checks the parts of the setup Escalated cannot default for the host: who may use the
/// admin and agent screens, and where the host's users come from. Each problem is one
/// sentence saying what is wrong and how to fix it. Logged at startup
/// (<see cref="EscalatedSetupCheckHostedService"/>) and reported by the
/// <c>escalated-setup</c> health check.
/// </summary>
public class EscalatedSetupCheck
{
    private readonly EscalatedDbContext _db;
    private readonly IAuthorizationPolicyProvider _policies;
    private readonly IUserDirectory _users;

    public EscalatedSetupCheck(EscalatedDbContext db, IAuthorizationPolicyProvider policies, IUserDirectory users)
    {
        _db = db;
        _policies = policies;
        _users = users;
    }

    public async Task<IReadOnlyList<string>> ProblemsAsync(CancellationToken ct = default)
    {
        var problems = new List<string>();

        // The host's users: names, emails and the admin users page come from IUserDirectory.
        if (_users is NullUserDirectory)
        {
            problems.Add(
                "No IUserDirectory is registered, so Escalated cannot list your users or show their names and emails " +
                "(people show by id and the admin users page is empty). Implement Escalated.Services.IUserDirectory over " +
                "your user table and register it: builder.Services.AddScoped<IUserDirectory, YourUserDirectory>(). " +
                "See \"Your users stay where they are\" in docs/getting-started.md.");
        }

        // The gates: Escalated's default policies grant the staff screens through its role table.
        if (await UsesRoleTableAsync(EscalatedPolicies.Admin)
            && !await AnyoneHoldsAsync(new[] { AdminUsersController.AdminRoleSlug }, ct))
        {
            problems.Add(
                "Nobody holds the escalated-admin role, so nobody can open the admin. Grant it to a user (a RoleUsers row " +
                "for the escalated-admin role) or register your own escalated-admin authorization policy. " +
                "See \"Granting access\" in docs/getting-started.md.");
        }

        if (await UsesRoleTableAsync(EscalatedPolicies.Agent)
            && !await AnyoneHoldsAsync(new[] { AdminUsersController.AgentRoleSlug, AdminUsersController.AdminRoleSlug }, ct))
        {
            problems.Add(
                "Nobody holds the escalated-agent or escalated-admin role, so nobody can open the agent workspace. Grant " +
                "one to your staff or register your own escalated-agent authorization policy. " +
                "See \"Granting access\" in docs/getting-started.md.");
        }

        return problems;
    }

    private async Task<bool> UsesRoleTableAsync(string policyName)
    {
        var policy = await _policies.GetPolicyAsync(policyName);
        return policy?.Requirements.OfType<EscalatedRoleRequirement>().Any() ?? false;
    }

    private Task<bool> AnyoneHoldsAsync(string[] slugs, CancellationToken ct) =>
        _db.RoleUsers.AnyAsync(ru => ru.Role != null && slugs.Contains(ru.Role.Slug), ct);
}

/// <summary>Logs each <see cref="EscalatedSetupCheck"/> problem as a warning when the app starts.</summary>
public sealed class EscalatedSetupCheckHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<EscalatedSetupCheckHostedService> _logger;

    public EscalatedSetupCheckHostedService(IServiceScopeFactory scopes, ILogger<EscalatedSetupCheckHostedService> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            foreach (var problem in await scope.ServiceProvider.GetRequiredService<EscalatedSetupCheck>().ProblemsAsync(cancellationToken))
            {
                _logger.LogWarning("Escalated setup: {Problem}", problem);
            }
        }
        catch (Exception failed) when (failed is not OperationCanceledException)
        {
            // The tables may not exist yet; the check is advisory.
            _logger.LogDebug(failed, "Escalated could not run its setup check.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>The <c>escalated-setup</c> health check: Degraded, listing each setup problem.</summary>
public sealed class EscalatedSetupHealthCheck : IHealthCheck
{
    public const string Name = "escalated-setup";

    private readonly IServiceScopeFactory _scopes;

    public EscalatedSetupHealthCheck(IServiceScopeFactory scopes)
    {
        _scopes = scopes;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var scope = _scopes.CreateScope();
        var problems = await scope.ServiceProvider.GetRequiredService<EscalatedSetupCheck>().ProblemsAsync(cancellationToken);
        if (problems.Count == 0)
        {
            return HealthCheckResult.Healthy("Escalated is set up.");
        }

        return HealthCheckResult.Degraded(
            string.Join(" ", problems),
            data: new Dictionary<string, object> { ["problems"] = problems });
    }
}
