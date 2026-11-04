using System.Text.Json;
using Escalated.Models;
using Escalated.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Escalated.Tests.Hosting.InertiaTestSupport;

namespace Escalated.Tests.Hosting;

/// <summary>
/// The props the frontend's current pages read that this package did not send:
/// <c>schedules</c> on the SLA policy and department forms (the business-hours
/// schedule picker, escalated-laravel #257), <c>required</c> on the two-factor
/// settings page (laravel #273), and <c>agents</c> on the agent ticket page.
/// </summary>
public class FormOptionsPropsTests
{
    private const string Admin = "admin-1";
    private const string Agent = "agent-1";

    private sealed class Directory : IUserDirectory
    {
        public Task<UserDirectoryPage> ListAsync(string? search, int page, int pageSize, CancellationToken ct = default) =>
            Task.FromResult(new UserDirectoryPage(Array.Empty<UserDirectoryEntry>(), 0, page, pageSize));

        public Task<UserDirectoryEntry?> FindAsync(string id, CancellationToken ct = default) => Task.FromResult<UserDirectoryEntry?>(id switch
        {
            Admin => new UserDirectoryEntry(Admin, "Ada Admin", "ada@example.com"),
            Agent => new UserDirectoryEntry(Agent, "Bea Agent", "bea@example.com"),
            _ => null,
        });
    }

    private sealed record Seeded(int PolicyId, int DepartmentId);

    private static async Task<(EscalatedTestHost Host, Seeded Seed)> StartAsync()
    {
        var host = await EscalatedTestHost.StartAsync(
            options =>
            {
                options.Ui.Enabled = true;
                options.Access.TwoFactor = true;
            },
            services => services.AddSingleton<IUserDirectory, Directory>());
        await host.GrantRoleAsync(Admin, "escalated-admin");
        await host.GrantRoleAsync(Agent, "escalated-agent");

        Seeded? seeded = null;
        await host.SeedAsync(async db =>
        {
            db.BusinessSchedules.AddRange(
                new BusinessSchedule { Name = "Weekend cover" },
                new BusinessSchedule { Name = "Office hours", IsDefault = true });
            var policy = new SlaPolicy { Name = "Standard", FirstResponseHours = "{}", ResolutionHours = "{}" };
            var department = new Department { Name = "Billing", Slug = "billing" };
            db.AddRange(policy, department);
            await db.SaveChangesAsync();
            db.Tickets.Add(new Ticket { Reference = "ESC-00001", Subject = "Card declined" });
            await db.SaveChangesAsync();
            seeded = new Seeded(policy.Id, department.Id);
        });

        return (host, seeded!);
    }

    private static void AssertSchedules(JsonElement page)
    {
        var schedules = page.GetProperty("props").GetProperty("schedules");
        Assert.Equal(new[] { "Office hours", "Weekend cover" },
            schedules.EnumerateArray().Select(s => s.GetProperty("name").GetString()));
        foreach (var schedule in schedules.EnumerateArray())
        {
            Assert.Equal(new[] { "id", "name" }, schedule.EnumerateObject().Select(p => p.Name));
            Assert.Equal(JsonValueKind.Number, schedule.GetProperty("id").ValueKind);
        }
    }

    [Theory]
    [InlineData("/support/admin/sla-policies/create", "Escalated/Admin/SlaPolicies/Form")]
    [InlineData("/support/admin/sla-policies/{policy}/edit", "Escalated/Admin/SlaPolicies/Form")]
    [InlineData("/support/admin/departments/create", "Escalated/Admin/Departments/Form")]
    [InlineData("/support/admin/departments/{department}/edit", "Escalated/Admin/Departments/Form")]
    public async Task TheFormsListTheBusinessSchedulesByName(string url, string component)
    {
        var (host, seed) = await StartAsync();
        await using var _ = host;

        var page = await PageAsync(host.ClientFor(Admin), url
            .Replace("{policy}", seed.PolicyId.ToString())
            .Replace("{department}", seed.DepartmentId.ToString()));

        AssertPage(page, component);
        AssertSchedules(page);
    }

    [Fact]
    public async Task TheTwoFactorPageSaysWhetherEnrolmentIsRequired()
    {
        var (host, _) = await StartAsync();
        await using var __ = host;

        var page = await PageAsync(host.ClientFor(Admin), "/support/admin/settings/two-factor");

        AssertPage(page, "Escalated/Admin/Settings/TwoFactor");
        Assert.False(page.GetProperty("props").GetProperty("required").GetBoolean());
    }

    [Fact]
    public async Task TheAgentTicketPageListsTheAssignableAgents()
    {
        var (host, _) = await StartAsync();
        await using var __ = host;

        var page = await PageAsync(host.ClientFor(Agent), "/support/agent/tickets/ESC-00001");

        AssertPage(page, "Escalated/Agent/TicketShow");
        var agents = page.GetProperty("props").GetProperty("agents").EnumerateArray()
            .Select(a => a.GetProperty("id").GetString()).ToList();
        Assert.Equal(new[] { Admin, Agent }, agents);
    }
}
