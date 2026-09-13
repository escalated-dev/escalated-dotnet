using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Escalated.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Escalated.Tests.Hosting;

/// <summary>
/// Who may change a saved view: its owner, or anyone when it has no owner.
///
/// <para><c>AdminSavedViewController</c> scoped the list to the signed-in admin's
/// own and shared views, but <c>Update</c> and <c>Delete</c> took an id and acted on
/// it, so any admin could rename, re-share or delete another admin's view. The
/// Laravel reference's <c>SavedViewController</c> refuses both with 403 unless the
/// view's <c>user_id</c> is empty or the caller's, shared or not: sharing lets
/// others use a view, not change it.</para>
/// </summary>
public class SavedViewOwnershipTests
{
    private const string AdminRole = "escalated-admin";
    private const string Owner = "admin-a";
    private const string OtherAdmin = "admin-b";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnotherAdmin_CannotUpdateTheView(bool shared)
    {
        await using var host = await StartAsync();
        var id = await SeedViewAsync(host, Owner, shared);

        var response = await host.ClientFor(OtherAdmin)
            .PutAsJsonAsync($"/support/admin/saved-views/{id}", new { name = "Taken over", isShared = !shared });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var view = await host.QueryAsync(db => db.SavedViews.SingleAsync(v => v.Id == id));
        Assert.Equal("Mine", view.Name);
        Assert.Equal(shared, view.IsShared);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnotherAdmin_CannotDeleteTheView(bool shared)
    {
        await using var host = await StartAsync();
        var id = await SeedViewAsync(host, Owner, shared);

        var response = await host.ClientFor(OtherAdmin).DeleteAsync($"/support/admin/saved-views/{id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True(await host.QueryAsync(db => db.SavedViews.AnyAsync(v => v.Id == id)));
    }

    [Fact]
    public async Task Owner_UpdatesAndDeletesTheirView()
    {
        await using var host = await StartAsync();
        var id = await SeedViewAsync(host, Owner, shared: false);
        var owner = host.ClientFor(Owner);

        var updated = await owner.PutAsJsonAsync($"/support/admin/saved-views/{id}", new { name = "Renamed" });

        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        using (var json = JsonDocument.Parse(await updated.Content.ReadAsStringAsync()))
        {
            Assert.Equal("Renamed", json.RootElement.GetProperty("name").GetString());
        }

        var deleted = await owner.DeleteAsync($"/support/admin/saved-views/{id}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.False(await host.QueryAsync(db => db.SavedViews.AnyAsync(v => v.Id == id)));
    }

    [Fact]
    public async Task AViewWithNoOwner_CanBeChangedByAnyAdmin()
    {
        await using var host = await StartAsync();
        var id = await SeedViewAsync(host, userId: null, shared: true);
        var admin = host.ClientFor(OtherAdmin);

        Assert.Equal(HttpStatusCode.OK,
            (await admin.PutAsJsonAsync($"/support/admin/saved-views/{id}", new { name = "Team queue" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/support/admin/saved-views/{id}")).StatusCode);
    }

    [Fact]
    public async Task AMissingView_IsNotFound()
    {
        await using var host = await StartAsync();
        var admin = host.ClientFor(OtherAdmin);

        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.PutAsJsonAsync("/support/admin/saved-views/999", new { name = "Nothing" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync("/support/admin/saved-views/999")).StatusCode);
    }

    private static async Task<EscalatedTestHost> StartAsync()
    {
        var host = await EscalatedTestHost.StartAsync();
        await host.GrantRoleAsync(Owner, AdminRole);
        await host.GrantRoleAsync(OtherAdmin, AdminRole);
        return host;
    }

    private static async Task<int> SeedViewAsync(EscalatedTestHost host, string? userId, bool shared)
    {
        var id = 0;
        await host.SeedAsync(async db =>
        {
            var view = new SavedView { Name = "Mine", UserId = userId, IsShared = shared, Filters = """{"status":"open"}""" };
            db.SavedViews.Add(view);
            await db.SaveChangesAsync();
            id = view.Id;
        });

        return id;
    }
}
