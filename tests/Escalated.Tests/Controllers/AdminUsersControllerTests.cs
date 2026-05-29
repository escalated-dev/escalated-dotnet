using Escalated.Controllers.Admin;
using Escalated.Data;
using Escalated.Models;
using Escalated.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Escalated.Tests.Controllers;

/// <summary>
/// Tests for <see cref="AdminUsersController"/> — the .NET port of the
/// canonical Laravel <c>Admin/UserController</c> (escalated-laravel#94).
///
/// The .NET plugin doesn't own a host User entity, so the controller
/// reads identity through an <see cref="IUserDirectory"/> (host-supplied
/// in production; stubbed here) and stores admin/agent membership in the
/// plugin's own <c>role_user</c> table. These 7 cases mirror the 7 Pest
/// cases in <c>tests/Feature/Admin/UserControllerTest.php</c> so any
/// behavior change here is easy to compare against the reference.
/// </summary>
public class AdminUsersControllerTests
{
    private static (AdminUsersController Controller, EscalatedDbContext Db, StubUserDirectory Directory) NewController()
    {
        var db = TestHelpers.CreateInMemoryDb();
        var directory = new StubUserDirectory();
        var controller = new AdminUsersController(db, directory);
        return (controller, db, directory);
    }

    [Fact]
    public async Task Index_ListsUsersWithAdminAgentFlags()
    {
        var (controller, db, directory) = NewController();
        var admin = directory.Add("1", "Admin", "admin@example.com");
        var customer = directory.Add("2", "Customer", "customer@example.com");
        var agent = directory.Add("3", "Agent", "agent@example.com");

        await GrantRoleAsync(db, admin.Id, AdminUsersController.AdminRoleSlug);
        await GrantRoleAsync(db, admin.Id, AdminUsersController.AgentRoleSlug);
        await GrantRoleAsync(db, agent.Id, AdminUsersController.AgentRoleSlug);

        var result = await controller.Index(search: null);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<AdminUsersController.IndexResponse>(ok.Value);
        var emails = response.Users.Data.Select(u => u.Email).ToList();
        Assert.Contains("admin@example.com", emails);
        Assert.Contains("customer@example.com", emails);
        Assert.Contains("agent@example.com", emails);

        // Admins are agents — the contract the Vue page consumes.
        var adminRow = response.Users.Data.Single(u => u.Email == "admin@example.com");
        Assert.True(adminRow.IsAdmin);
        Assert.True(adminRow.IsAgent);

        var agentRow = response.Users.Data.Single(u => u.Email == "agent@example.com");
        Assert.False(agentRow.IsAdmin);
        Assert.True(agentRow.IsAgent);

        var customerRow = response.Users.Data.Single(u => u.Email == "customer@example.com");
        Assert.False(customerRow.IsAdmin);
        Assert.False(customerRow.IsAgent);
    }

    [Fact]
    public async Task Index_SortsAdminsFirstThenAgentsThenById()
    {
        var (controller, db, directory) = NewController();
        // Insert out-of-order to prove the controller re-sorts the page.
        var customer = directory.Add("1", "Customer", "customer@example.com");
        var admin = directory.Add("2", "Admin", "admin@example.com");
        var agent = directory.Add("3", "Agent", "agent@example.com");

        await GrantRoleAsync(db, admin.Id, AdminUsersController.AdminRoleSlug);
        await GrantRoleAsync(db, agent.Id, AdminUsersController.AgentRoleSlug);

        var result = await controller.Index(search: null);
        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<AdminUsersController.IndexResponse>(ok.Value);
        var ordered = response.Users.Data.Select(u => u.Email).ToList();

        Assert.Equal(new[]
        {
            "admin@example.com",
            "agent@example.com",
            "customer@example.com",
        }, ordered);
    }

    [Fact]
    public async Task UpdateRole_PromotesUserToAdmin_AlsoGrantsAgent()
    {
        var (controller, db, directory) = NewController();
        var target = directory.Add("10", "Target", "target@example.com");

        var result = await controller.UpdateRole(
            target.Id,
            new AdminUsersController.UpdateRoleRequest("admin", true));

        Assert.IsType<OkObjectResult>(result);

        Assert.True(await HasRoleAsync(db, target.Id, AdminUsersController.AdminRoleSlug));
        Assert.True(await HasRoleAsync(db, target.Id, AdminUsersController.AgentRoleSlug));
    }

    [Fact]
    public async Task UpdateRole_PromotesUserToAgentOnly()
    {
        var (controller, db, directory) = NewController();
        var target = directory.Add("11", "Target", "target@example.com");

        var result = await controller.UpdateRole(
            target.Id,
            new AdminUsersController.UpdateRoleRequest("agent", true));

        Assert.IsType<OkObjectResult>(result);

        Assert.False(await HasRoleAsync(db, target.Id, AdminUsersController.AdminRoleSlug));
        Assert.True(await HasRoleAsync(db, target.Id, AdminUsersController.AgentRoleSlug));
    }

    [Fact]
    public async Task UpdateRole_PreventsAdminFromDemotingThemselves()
    {
        var (controller, db, directory) = NewController();
        var admin = directory.Add("42", "Admin", "admin@example.com");
        await GrantRoleAsync(db, admin.Id, AdminUsersController.AdminRoleSlug);
        await GrantRoleAsync(db, admin.Id, AdminUsersController.AgentRoleSlug);

        var result = await controller.UpdateRole(
            admin.Id,
            new AdminUsersController.UpdateRoleRequest("admin", false),
            currentUserId: admin.Id);

        Assert.IsType<BadRequestObjectResult>(result);

        // Role survived the attempted self-demote — the admin gate they're
        // using to make the request stays on.
        Assert.True(await HasRoleAsync(db, admin.Id, AdminUsersController.AdminRoleSlug));
    }

    [Fact]
    public async Task UpdateRole_DemotingAgentOffOfAnAdmin_AlsoRevokesAdmin()
    {
        var (controller, db, directory) = NewController();
        var target = directory.Add("50", "Target", "target@example.com");
        await GrantRoleAsync(db, target.Id, AdminUsersController.AdminRoleSlug);
        await GrantRoleAsync(db, target.Id, AdminUsersController.AgentRoleSlug);

        var result = await controller.UpdateRole(
            target.Id,
            new AdminUsersController.UpdateRoleRequest("agent", false));

        Assert.IsType<OkObjectResult>(result);

        // Both gates flip off in one step — otherwise the admin gate would
        // stay on while the agent gate was off.
        Assert.False(await HasRoleAsync(db, target.Id, AdminUsersController.AgentRoleSlug));
        Assert.False(await HasRoleAsync(db, target.Id, AdminUsersController.AdminRoleSlug));
    }

    [Fact]
    public async Task Index_FiltersUsersBySearchTerm()
    {
        var (controller, _, directory) = NewController();
        directory.Add("1", "Admin", "admin@example.com");
        directory.Add("2", "Jane", "jane@acme.test");
        directory.Add("3", "Bob", "bob@globex.test");

        var result = await controller.Index(search: "acme");

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<AdminUsersController.IndexResponse>(ok.Value);
        var emails = response.Users.Data.Select(u => u.Email).ToList();
        Assert.Contains("jane@acme.test", emails);
        Assert.DoesNotContain("bob@globex.test", emails);
        Assert.Equal("acme", response.Filters.Search);
    }

    private static async Task GrantRoleAsync(EscalatedDbContext db, string userId, string slug)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Slug == slug)
            ?? db.Roles.Add(new Role
            {
                Name = slug,
                Slug = slug,
                IsSystem = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            }).Entity;
        await db.SaveChangesAsync();
        db.RoleUsers.Add(new RoleUser { UserId = userId, RoleId = role.Id });
        await db.SaveChangesAsync();
    }

    private static async Task<bool> HasRoleAsync(EscalatedDbContext db, string userId, string slug)
    {
        return await db.RoleUsers
            .AnyAsync(ru => ru.UserId == userId && ru.Role != null && ru.Role.Slug == slug);
    }

    /// <summary>
    /// Test double for <see cref="IUserDirectory"/>. Implements the
    /// search-on-name-OR-email contract the controller relies on so the
    /// "filters users by search term" case lines up with the Laravel
    /// reference's <c>->where('email','like',$term)->orWhere('name','like',$term)</c>.
    /// </summary>
    private class StubUserDirectory : IUserDirectory
    {
        private readonly List<UserDirectoryEntry> _users = new();

        public UserDirectoryEntry Add(string id, string? name, string? email)
        {
            var entry = new UserDirectoryEntry(id, name, email);
            _users.Add(entry);
            return entry;
        }

        public Task<UserDirectoryPage> ListAsync(string? search, int page, int pageSize, CancellationToken ct = default)
        {
            IEnumerable<UserDirectoryEntry> filtered = _users;
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLowerInvariant();
                filtered = filtered.Where(u =>
                    (u.Email ?? string.Empty).ToLowerInvariant().Contains(term)
                    || (u.Name ?? string.Empty).ToLowerInvariant().Contains(term));
            }

            var all = filtered.OrderBy(u => u.Id).ToList();
            var items = all.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return Task.FromResult(new UserDirectoryPage(items, all.Count, page, pageSize));
        }

        public Task<UserDirectoryEntry?> FindAsync(string id, CancellationToken ct = default)
            => Task.FromResult(_users.FirstOrDefault(u => u.Id == id));
    }
}
