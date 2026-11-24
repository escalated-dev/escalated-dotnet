using System.Net;
using Escalated.Authorization;
using Escalated.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace Escalated.Tests.Hosting;

/// <summary>
/// A host whose setup Escalated cannot use gets told what is wrong: a staff request whose
/// user cannot be identified fails with <see cref="EscalatedUserNotResolvedException"/>
/// instead of running as nobody, and <see cref="EscalatedSetupCheck"/> (logged at startup,
/// the <c>escalated-setup</c> health check) names missing admins/agents and a missing
/// <see cref="IUserDirectory"/>.
/// </summary>
public class SetupCheckTests
{
    private sealed class Directory : IUserDirectory
    {
        public Task<UserDirectoryPage> ListAsync(string? search, int page, int pageSize, CancellationToken ct = default) =>
            Task.FromResult(new UserDirectoryPage(Array.Empty<UserDirectoryEntry>(), 0, page, pageSize));

        public Task<UserDirectoryEntry?> FindAsync(string id, CancellationToken ct = default) =>
            Task.FromResult<UserDirectoryEntry?>(null);
    }

    /// <summary>A host that replaced the agent policy and keeps its user id where Escalated does not look.</summary>
    private static Task<EscalatedTestHost> StartWithUnresolvableUsersAsync() => EscalatedTestHost.StartAsync(
        options => options.UserIdResolver = _ => null,
        services => services.AddAuthorization(o =>
        {
            o.AddPolicy(EscalatedPolicies.Agent, p => p.RequireAuthenticatedUser());
            o.AddPolicy(EscalatedPolicies.Admin, p => p.RequireAuthenticatedUser());
        }));

    [Theory]
    [InlineData("/support/agent/tickets")]
    [InlineData("/support/admin/tickets")]
    public async Task StaffRequestWithNoResolvableUser_FailsWithAClearError(string url)
    {
        await using var host = await StartWithUnresolvableUsersAsync();

        var failure = await Assert.ThrowsAsync<EscalatedUserNotResolvedException>(() => host.ClientFor("agent-1").GetAsync(url));

        Assert.Contains("NameIdentifier", failure.Message);
        Assert.Contains("UserIdResolver", failure.Message);
        Assert.Contains("Who a request acts as", failure.Message);
    }

    [Fact]
    public async Task StaffRequestWithAResolvableUser_IsServed()
    {
        await using var host = await EscalatedTestHost.StartAsync(
            configureServices: services => services.AddAuthorization(o =>
                o.AddPolicy(EscalatedPolicies.Agent, p => p.RequireAuthenticatedUser())));

        var response = await host.ClientFor("agent-1").GetAsync("/support/agent/tickets");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UnresolvableUser_OnACustomerRoute_IsStillRefusedAsBefore()
    {
        await using var host = await StartWithUnresolvableUsersAsync();

        var response = await host.ClientFor("customer-1").GetAsync("/support/tickets");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SetupCheck_ReportsNoAdminsNoAgentsAndNoUserDirectory()
    {
        await using var host = await EscalatedTestHost.StartAsync();
        using var scope = host.Services.CreateScope();

        var problems = await scope.ServiceProvider.GetRequiredService<EscalatedSetupCheck>().ProblemsAsync();

        Assert.Equal(3, problems.Count);
        Assert.Contains(problems, p => p.Contains("IUserDirectory") && p.Contains("AddScoped<IUserDirectory"));
        Assert.Contains(problems, p => p.Contains("Nobody holds the escalated-admin role"));
        Assert.Contains(problems, p => p.Contains("agent workspace"));

        var report = await host.Services.GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(r => r.Name == EscalatedSetupHealthCheck.Name);
        Assert.Equal(HealthStatus.Degraded, report.Entries[EscalatedSetupHealthCheck.Name].Status);
    }

    [Fact]
    public async Task SetupCheck_IsCleanOnceAdminsAndTheDirectoryExist()
    {
        await using var host = await EscalatedTestHost.StartAsync(
            configureServices: services => services.AddSingleton<IUserDirectory, Directory>());
        await host.GrantRoleAsync("admin-1", "escalated-admin");
        using var scope = host.Services.CreateScope();

        Assert.Empty(await scope.ServiceProvider.GetRequiredService<EscalatedSetupCheck>().ProblemsAsync());

        var report = await host.Services.GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(r => r.Name == EscalatedSetupHealthCheck.Name);
        Assert.Equal(HealthStatus.Healthy, report.Entries[EscalatedSetupHealthCheck.Name].Status);
    }

    [Fact]
    public async Task SetupCheck_LeavesHostPoliciesAlone()
    {
        await using var host = await EscalatedTestHost.StartAsync(
            configureServices: services =>
            {
                services.AddSingleton<IUserDirectory, Directory>();
                services.AddAuthorization(o =>
                {
                    o.AddPolicy(EscalatedPolicies.Admin, p => p.RequireRole("SupportLead"));
                    o.AddPolicy(EscalatedPolicies.Agent, p => p.RequireRole("SupportAgent"));
                });
            });
        using var scope = host.Services.CreateScope();

        Assert.Empty(await scope.ServiceProvider.GetRequiredService<EscalatedSetupCheck>().ProblemsAsync());
    }
}
