using System.Net;
using System.Text.Json;
using Escalated.Authorization;
using Escalated.Models;
using Escalated.Services;
using Escalated.Tests.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Escalated.Tests.Hosting.InertiaTestSupport;

namespace Escalated.Tests.Access;

/// <summary>
/// The users screen's roles picker (<c>PUT escalated.admin.users.roles</c> with
/// <c>{role_ids: [...]}</c>), against escalated-laravel's <c>UserController::updateRoles</c>:
/// only while roles and permissions is on, needs <c>role.manage</c>, validates the ids,
/// refuses removing your own role management and goes back with "User updated.". The gate
/// roles (<c>escalated-admin</c>, <c>escalated-agent</c>) are not Escalated roles here and are
/// neither listed nor touched.
/// </summary>
public class AdminUserRolesTests
{
    private const string Admin = "1";
    private const string Agent = "2";
    private const string Plain = "3";

    private sealed class Directory : IUserDirectory
    {
        private static readonly List<UserDirectoryEntry> Users = new()
        {
            new(Admin, "Ada Admin", "ada@example.com"),
            new(Agent, "Avery Agent", "avery@example.com"),
            new(Plain, "Pat Plain", "pat@example.com"),
        };

        public Task<UserDirectoryPage> ListAsync(string? search, int page, int pageSize, CancellationToken ct = default) =>
            Task.FromResult(new UserDirectoryPage(Users.Skip((page - 1) * pageSize).Take(pageSize).ToList(), Users.Count, page, pageSize));

        public Task<UserDirectoryEntry?> FindAsync(string id, CancellationToken ct = default) =>
            Task.FromResult(Users.FirstOrDefault(u => u.Id == id));
    }

    private static async Task<EscalatedTestHost> StartAsync(bool on)
    {
        var host = await EscalatedTestHost.StartAsync(
            options =>
            {
                options.Ui.Enabled = true;
                options.Access.RolesPermissions = on;
            },
            services => services.AddSingleton<IUserDirectory, Directory>());
        using (var scope = host.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<PermissionSeeder>().SeedAsync();
        }

        await host.GrantRoleAsync(Admin, "escalated-admin");
        await host.GrantRoleAsync(Admin, "admin");
        await host.GrantRoleAsync(Agent, "escalated-agent");
        await host.GrantRoleAsync(Agent, "agent");
        return host;
    }

    private static Task<int> RoleIdAsync(EscalatedTestHost host, string slug) =>
        host.QueryAsync(db => db.Roles.Where(r => r.Slug == slug).Select(r => r.Id).SingleAsync());

    private static Task<List<string>> SlugsAsync(EscalatedTestHost host, string userId) =>
        host.QueryAsync(db => db.RoleUsers.Where(ru => ru.UserId == userId).Select(ru => ru.Role!.Slug).OrderBy(s => s).ToListAsync());

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, string userId, object body) =>
        client.SendAsync(Visit(HttpMethod.Put, $"/support/admin/users/{userId}/roles", body, "http://localhost/support/admin/users"));

    [Fact]
    public async Task Off_TheRouteAnswers404AndTheScreenSendsNoRoles()
    {
        await using var host = await StartAsync(on: false);
        var client = host.ClientFor(Admin);
        var light = await RoleIdAsync(host, "light_agent");

        Assert.Equal(HttpStatusCode.NotFound, (await PutAsync(client, Agent, new { role_ids = new[] { light } })).StatusCode);
        Assert.Equal(new[] { "agent", "escalated-agent" }, await SlugsAsync(host, Agent));

        var page = await PageAsync(client, "/support/admin/users");
        AssertPage(page, "Escalated/Admin/Users/Index");
        Assert.Equal(JsonValueKind.Null, page.GetProperty("props").GetProperty("roles").ValueKind);
        Assert.False(page.GetProperty("props").GetProperty("users").GetProperty("data")[0].TryGetProperty("roles", out _));
    }

    [Fact]
    public async Task On_SendsTheRolesAndEachUsersRoleSlugs_WithoutTheGateRoles()
    {
        await using var host = await StartAsync(on: true);
        var page = await PageAsync(host.ClientFor(Admin), "/support/admin/users");
        AssertPage(page, "Escalated/Admin/Users/Index");
        var props = page.GetProperty("props");

        var roles = props.GetProperty("roles").EnumerateArray().ToList();
        var names = roles.Select(r => r.GetProperty("name").GetString()!).ToList();
        Assert.Equal(names.OrderBy(n => n, StringComparer.Ordinal).ToList(), names);
        Assert.Equal(new[] { "id", "name", "slug" }, roles[0].EnumerateObject().Select(p => p.Name).OrderBy(n => n));
        var slugs = roles.Select(r => r.GetProperty("slug").GetString()).ToList();
        Assert.Contains("light_agent", slugs);
        Assert.DoesNotContain("escalated-admin", slugs);
        Assert.DoesNotContain("escalated-agent", slugs);

        var rows = props.GetProperty("users").GetProperty("data").EnumerateArray()
            .ToDictionary(r => r.GetProperty("id").GetString()!, r => r.GetProperty("roles").EnumerateArray().Select(s => s.GetString()).ToList());
        Assert.Equal(new[] { "admin" }, rows[Admin]);
        Assert.Equal(new[] { "agent" }, rows[Agent]);
        Assert.Empty(rows[Plain]);
    }

    [Fact]
    public async Task On_ReplacesTheUsersRoles_KeepsTheGateRoles_AndGoesBackWithTheFlash()
    {
        await using var host = await StartAsync(on: true);
        var client = host.ClientFor(Admin);
        var light = await RoleIdAsync(host, "light_agent");
        var agent = await RoleIdAsync(host, "agent");

        var response = await PutAsync(client, Agent, new { role_ids = new[] { light } });
        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        Assert.Equal("/support/admin/users", response.Headers.Location!.OriginalString);
        var next = await PageAsync(client, "/support/admin/users", FlashCookie(response));
        Assert.Equal("User updated.", next.GetProperty("props").GetProperty("flash").GetProperty("success").GetString());
        Assert.Equal(new[] { "escalated-agent", "light_agent" }, await SlugsAsync(host, Agent));

        using (var scope = host.Services.CreateScope())
        {
            var granted = await scope.ServiceProvider.GetRequiredService<AccessControl>().PermissionsOfAsync(Agent);
            Assert.Contains("reply.create_internal", granted);
            Assert.DoesNotContain("ticket.assign", granted);
        }

        await PutAsync(client, Agent, new { role_ids = new[] { light, agent, light } });
        Assert.Equal(new[] { "agent", "escalated-agent", "light_agent" }, await SlugsAsync(host, Agent));

        await PutAsync(client, Agent, new { role_ids = Array.Empty<int>() });
        Assert.Equal(new[] { "escalated-agent" }, await SlugsAsync(host, Agent));
    }

    [Theory]
    [InlineData("{}", "role_ids")]
    [InlineData("{\"role_ids\":\"admin\"}", "role_ids")]
    [InlineData("{\"role_ids\":[\"x\"]}", "role_ids.0")]
    [InlineData("{\"role_ids\":[987654]}", "role_ids.0")]
    [InlineData("{\"role_ids\":[\"gate\"]}", "role_ids.0")]
    public async Task On_ValidatesRoleIdsAsLaravelDoes(string json, string key)
    {
        await using var host = await StartAsync(on: true);
        var client = host.ClientFor(Admin);
        var gate = await RoleIdAsync(host, "escalated-admin");
        var body = JsonSerializer.Deserialize<JsonElement>(json.Replace("\"gate\"", gate.ToString()));

        var response = await PutAsync(client, Agent, body);
        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        var next = await PageAsync(client, "/support/admin/users", FlashCookie(response));
        var errors = next.GetProperty("props").GetProperty("errors").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(new[] { key }, errors);
        Assert.Equal(new[] { "agent", "escalated-agent" }, await SlugsAsync(host, Agent));
    }

    [Fact]
    public async Task On_NeedsRoleManage()
    {
        await using var host = await StartAsync(on: true);
        await host.SeedAsync(async db =>
        {
            var role = new Role { Name = "User admins", Slug = "user-admins" };
            db.Roles.Add(role);
            await db.SaveChangesAsync();
            foreach (var permission in await db.Permissions.Where(p => p.Slug == "user.view" || p.Slug == "user.manage").ToListAsync())
            {
                db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id });
            }

            await db.SaveChangesAsync();
        });
        await host.GrantRoleAsync("9", "escalated-admin");
        await host.GrantRoleAsync("9", "user-admins");
        var light = await RoleIdAsync(host, "light_agent");

        var response = await PutAsync(host.ClientFor("9"), Agent, new { role_ids = new[] { light } });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(new[] { "agent", "escalated-agent" }, await SlugsAsync(host, Agent));
    }

    [Fact]
    public async Task On_ARoleManagerCannotRemoveTheirOwnRoleManagement()
    {
        await using var host = await StartAsync(on: true);
        var client = host.ClientFor(Admin);
        var light = await RoleIdAsync(host, "light_agent");

        var response = await PutAsync(client, Admin, new { role_ids = new[] { light } });
        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        var next = await PageAsync(client, "/support/admin/users", FlashCookie(response));
        Assert.Equal("You cannot remove your own role management.", next.GetProperty("props").GetProperty("flash").GetProperty("error").GetString());
        Assert.Equal(new[] { "admin", "escalated-admin" }, await SlugsAsync(host, Admin));
    }

    [Fact]
    public async Task On_AnUnknownUserIs404()
    {
        await using var host = await StartAsync(on: true);
        var response = await PutAsync(host.ClientFor(Admin), "404", new { role_ids = Array.Empty<int>() });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
