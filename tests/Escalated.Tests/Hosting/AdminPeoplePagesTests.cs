using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Escalated.Middleware;
using Escalated.Models;
using Escalated.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Escalated.Tests.Hosting.InertiaTestSupport;

namespace Escalated.Tests.Hosting;

/// <summary>
/// The people pages (<c>Admin/Users/Index</c>, <c>Admin/Roles/Index</c> and
/// <c>Form</c>, <c>Admin/ApiTokens/Index</c>) against escalated-laravel's
/// <c>UserController</c>, <c>RoleController</c> and <c>ApiTokenController</c>, plus
/// the JSON admin API on the shared paths staying as it was.
/// </summary>
public class AdminPeoplePagesTests
{
    private const string Admin = "1";
    private const string Agent = "2";
    private const string Customer = "3";

    private sealed class Directory : IUserDirectory
    {
        private static readonly List<UserDirectoryEntry> Users = new()
        {
            new(Admin, "Ada Admin", "ada@example.com"),
            new(Agent, "Avery Agent", "avery@example.com"),
            new(Customer, "Casey Customer", "casey@example.com"),
        };

        public Task<UserDirectoryPage> ListAsync(string? search, int page, int pageSize, CancellationToken ct = default)
        {
            var matches = Users.Where(u => search is null || u.Name!.Contains(search, StringComparison.OrdinalIgnoreCase)
                || u.Email!.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
            return Task.FromResult(new UserDirectoryPage(matches.Skip((page - 1) * pageSize).Take(pageSize).ToList(), matches.Count, page, pageSize));
        }

        public Task<UserDirectoryEntry?> FindAsync(string id, CancellationToken ct = default) =>
            Task.FromResult(Users.FirstOrDefault(u => u.Id == id));
    }

    private sealed record Seeded(int RoleId, int SystemRoleId, int PermissionId, int OtherPermissionId, int TokenId);

    private static async Task<(EscalatedTestHost Host, HttpClient Client, Seeded Seed)> StartAsync(bool apiWired = false)
    {
        var host = await EscalatedTestHost.StartAsync(
            options => options.Ui.Enabled = true,
            services => services.AddSingleton<IUserDirectory, Directory>(),
            apiWired ? app => app.UseWhen(c => c.Request.Path.StartsWithSegments("/api-probe"), b => b.UseMiddleware<ApiTokenAuthMiddleware>()) : null);
        await host.GrantRoleAsync(Admin, "escalated-admin");
        await host.GrantRoleAsync(Agent, "escalated-agent");

        Seeded? seeded = null;
        await host.SeedAsync(async db =>
        {
            var view = new Permission { Name = "View newsletters", Slug = "newsletters.view", Group = "Newsletters" };
            var send = new Permission { Name = "Send newsletters", Slug = "newsletters.send", Group = "Newsletters" };
            var role = new Role { Name = "Editors", Slug = "editors", Description = "Write things" };
            db.AddRange(view, send, role);
            await db.SaveChangesAsync();
            db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = view.Id });
            db.RoleUsers.Add(new RoleUser { RoleId = role.Id, UserId = Agent });
            var token = new ApiToken { Name = "CI", TokenHash = "hash", TokenableType = "User", TokenableId = 2, Abilities = """["agent"]""", ExpiresAt = DateTime.UtcNow.AddDays(-1) };
            db.ApiTokens.Add(token);
            await db.SaveChangesAsync();

            var system = await db.Roles.SingleAsync(r => r.Slug == "escalated-admin");
            seeded = new Seeded(role.Id, system.Id, view.Id, send.Id, token.Id);
        });

        return (host, host.ClientFor(Admin), seeded!);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string url, object? body = null)
    {
        var response = await client.SendAsync(Visit(method, url, body ?? new { }));
        Assert.True(response.StatusCode == HttpStatusCode.SeeOther, $"{method} {url}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return response;
    }

    // --- Users ---

    [Fact]
    public async Task Users_RendersThePagedUsersWithTheirRoles()
    {
        var (host, client, _) = await StartAsync();
        await using var __ = host;

        var page = await PageAsync(client, "/support/admin/users");
        AssertPage(page, "Escalated/Admin/Users/Index");
        var props = page.GetProperty("props");
        var users = Keys(props.GetProperty("users"), "data", "current_page", "last_page", "per_page", "total", "links");
        var rows = users.GetProperty("data").EnumerateArray().ToList();
        Assert.Equal(new[] { Admin, Agent, Customer }, rows.Select(r => r.GetProperty("id").GetString()));
        var first = Keys(rows[0], "id", "name", "email", "is_admin", "is_agent");
        Assert.True(first.GetProperty("is_admin").GetBoolean());
        Assert.True(first.GetProperty("is_agent").GetBoolean());
        Assert.False(rows[2].GetProperty("is_agent").GetBoolean());
        Assert.Equal(Admin, props.GetProperty("currentUserId").GetString());
        Assert.Equal("", props.GetProperty("filters").GetProperty("search").GetString());

        var searched = await PageAsync(client, "/support/admin/users?search=casey");
        Assert.Equal(1, searched.GetProperty("props").GetProperty("users").GetProperty("total").GetInt32());
        Assert.Equal("casey", searched.GetProperty("props").GetProperty("filters").GetProperty("search").GetString());
    }

    [Fact]
    public async Task Users_RoleToggleAnswersThePageWithARedirect()
    {
        var (host, client, _) = await StartAsync();
        await using var __ = host;

        var grant = await SendAsync(client, HttpMethod.Patch, $"/support/admin/users/{Customer}/role", new { role = "agent", value = true });
        var page = await PageAsync(client, "/support/admin/users", FlashCookie(grant));
        Assert.Equal("User updated.", page.GetProperty("props").GetProperty("flash").GetProperty("success").GetString());
        Assert.True(page.GetProperty("props").GetProperty("users").GetProperty("data").EnumerateArray()
            .Single(u => u.GetProperty("id").GetString() == Customer).GetProperty("is_agent").GetBoolean());

        var self = await SendAsync(client, HttpMethod.Patch, $"/support/admin/users/{Admin}/role", new { role = "admin", value = false });
        var after = await PageAsync(client, "/support/admin/users", FlashCookie(self));
        Assert.Equal("You cannot remove your own admin role.", after.GetProperty("props").GetProperty("flash").GetProperty("error").GetString());
    }

    [Fact]
    public async Task Users_JsonApiIsUnchanged()
    {
        var (host, client, _) = await StartAsync();
        await using var __ = host;

        var list = await client.GetFromJsonAsync<JsonElement>("/support/admin/users");
        Assert.Equal(3, list.GetProperty("users").GetProperty("total").GetInt32());

        var patch = await client.PatchAsJsonAsync($"/support/admin/users/{Admin}/role", new { role = "admin", value = false });
        Assert.Equal(HttpStatusCode.BadRequest, patch.StatusCode);
    }

    // --- Roles ---

    [Fact]
    public async Task Roles_IndexAndFormRenderRolesAndGroupedPermissions()
    {
        var (host, client, seed) = await StartAsync();
        await using var __ = host;

        var index = await PageAsync(client, "/support/admin/roles");
        AssertPage(index, "Escalated/Admin/Roles/Index");
        var roles = index.GetProperty("props").GetProperty("roles").EnumerateArray().ToList();
        var editors = Keys(roles.Single(r => r.GetProperty("slug").GetString() == "editors"), "id", "name", "slug", "description", "is_system", "users_count");
        Assert.Equal(1, editors.GetProperty("users_count").GetInt32());
        Assert.True(roles.Single(r => r.GetProperty("slug").GetString() == "escalated-admin").GetProperty("is_system").GetBoolean());

        var create = await PageAsync(client, "/support/admin/roles/create");
        AssertPage(create, "Escalated/Admin/Roles/Form");
        Assert.Equal(JsonValueKind.Null, create.GetProperty("props").GetProperty("role").ValueKind);
        var group = create.GetProperty("props").GetProperty("permissions").GetProperty("Newsletters");
        Keys(group[0], "id", "name", "slug", "group");
        Assert.Equal(2, group.GetArrayLength());

        var edit = await PageAsync(client, $"/support/admin/roles/{seed.RoleId}/edit");
        var role = edit.GetProperty("props").GetProperty("role");
        Assert.Equal(seed.PermissionId, Assert.Single(role.GetProperty("permissions").EnumerateArray()).GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task Roles_StoreUpdateAndDestroy()
    {
        var (host, client, seed) = await StartAsync();
        await using var __ = host;

        await SendAsync(client, HttpMethod.Post, "/support/admin/roles", new { name = "Reviewers", description = "Read only", permissions = new[] { seed.OtherPermissionId } });
        var created = await host.QueryAsync(db => db.Roles.Include(r => r.Permissions).AsNoTracking().SingleAsync(r => r.Name == "Reviewers"));
        Assert.Equal("reviewers", created.Slug);
        Assert.Equal(seed.OtherPermissionId, Assert.Single(created.Permissions).Id);

        await SendAsync(client, HttpMethod.Put, $"/support/admin/roles/{seed.RoleId}", new { name = "Editors+", permissions = new[] { seed.PermissionId, seed.OtherPermissionId } });
        var updated = await host.QueryAsync(db => db.Roles.Include(r => r.Permissions).AsNoTracking().SingleAsync(r => r.Id == seed.RoleId));
        Assert.Equal("Editors+", updated.Name);
        Assert.Equal(2, updated.Permissions.Count);

        var duplicate = await SendAsync(client, HttpMethod.Post, "/support/admin/roles", new { name = "reviewers" });
        var errors = (await PageAsync(client, "/support/admin/roles/create", FlashCookie(duplicate))).GetProperty("props").GetProperty("errors");
        Keys(errors, "name");
        var unknown = await SendAsync(client, HttpMethod.Post, "/support/admin/roles", new { name = "Ghosts", permissions = new[] { 999 } });
        Keys((await PageAsync(client, "/support/admin/roles/create", FlashCookie(unknown))).GetProperty("props").GetProperty("errors"), "permissions");

        var system = await SendAsync(client, HttpMethod.Delete, $"/support/admin/roles/{seed.SystemRoleId}");
        Assert.True(await host.QueryAsync(db => db.Roles.AnyAsync(r => r.Id == seed.SystemRoleId)));
        var flash = (await PageAsync(client, "/support/admin/roles", FlashCookie(system))).GetProperty("props").GetProperty("flash");
        Assert.Equal("System roles cannot be deleted.", flash.GetProperty("error").GetString());

        await SendAsync(client, HttpMethod.Delete, $"/support/admin/roles/{seed.RoleId}");
        Assert.False(await host.QueryAsync(db => db.Roles.AnyAsync(r => r.Id == seed.RoleId)));
        Assert.False(await host.QueryAsync(db => db.RoleUsers.AnyAsync(ru => ru.RoleId == seed.RoleId)));
    }

    // --- API tokens ---

    [Fact]
    public async Task ApiTokens_IndexListsTokensAndStaff()
    {
        var (host, client, _) = await StartAsync();
        await using var __ = host;

        var page = await PageAsync(client, "/support/admin/api-tokens");
        AssertPage(page, "Escalated/Admin/ApiTokens/Index");
        var props = page.GetProperty("props");
        var token = Keys(props.GetProperty("tokens")[0], "id", "name", "user_name", "user_email", "abilities", "last_used_at", "expires_at", "is_expired", "created_at");
        Assert.Equal("Avery Agent", token.GetProperty("user_name").GetString());
        Assert.Equal("agent", token.GetProperty("abilities")[0].GetString());
        Assert.True(token.GetProperty("is_expired").GetBoolean());
        Assert.Equal(new[] { "Ada Admin", "Avery Agent" }, props.GetProperty("users").EnumerateArray().Select(u => u.GetProperty("name").GetString()));
        Assert.False(props.GetProperty("api_enabled").GetBoolean());
    }

    [Fact]
    public async Task ApiTokens_ReportTheApiAsEnabledWhenTheHostWiresTokenAuth()
    {
        var (host, client, _) = await StartAsync(apiWired: true);
        await using var __ = host;

        var page = await PageAsync(client, "/support/admin/api-tokens");

        Assert.True(page.GetProperty("props").GetProperty("api_enabled").GetBoolean());
    }

    [Fact]
    public async Task ApiTokens_CreateFlashesThePlainTokenOnceAndRevokeDeletes()
    {
        var (host, client, seed) = await StartAsync();
        await using var __ = host;

        var created = await SendAsync(client, HttpMethod.Post, "/support/admin/api-tokens",
            new { name = "Deploy bot", user_id = Admin, abilities = new[] { "agent", "admin" }, expires_in_days = 30 });

        var page = await PageAsync(client, "/support/admin/api-tokens", FlashCookie(created));
        var plain = page.GetProperty("flash").GetProperty("plain_text_token").GetString();
        Assert.False(string.IsNullOrEmpty(plain));
        var token = await host.QueryAsync(db => db.ApiTokens.AsNoTracking().SingleAsync(t => t.Name == "Deploy bot"));
        Assert.Equal(ApiTokenAuthMiddleware.ComputeSha256(plain!), token.TokenHash);
        Assert.Equal(1, token.TokenableId);
        Assert.Equal("""["agent","admin"]""", token.Abilities);
        Assert.True(token.ExpiresAt > DateTime.UtcNow.AddDays(29));

        var again = await PageAsync(client, "/support/admin/api-tokens");
        Assert.False(again.GetProperty("flash").TryGetProperty("plain_text_token", out _));

        var invalid = await SendAsync(client, HttpMethod.Post, "/support/admin/api-tokens",
            new { name = "", user_id = "abc", abilities = new[] { "root" }, expires_in_days = 999 });
        Keys((await PageAsync(client, "/support/admin/api-tokens", FlashCookie(invalid))).GetProperty("props").GetProperty("errors"),
            "name", "user_id", "abilities", "expires_in_days");

        await SendAsync(client, HttpMethod.Delete, $"/support/admin/api-tokens/{seed.TokenId}");
        Assert.False(await host.QueryAsync(db => db.ApiTokens.AnyAsync(t => t.Id == seed.TokenId)));
    }

    [Theory]
    [InlineData("/support/admin/roles")]
    [InlineData("/support/admin/api-tokens")]
    public async Task TheJsonApi_StillAnswersApiClients(string url)
    {
        var (host, client, _) = await StartAsync();
        await using var __ = host;

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("X-Inertia"));
        Assert.Equal(JsonValueKind.Array, JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.ValueKind);
    }
}
