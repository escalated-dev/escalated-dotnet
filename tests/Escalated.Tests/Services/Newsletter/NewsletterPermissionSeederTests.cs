using Escalated.Controllers.Admin;
using Escalated.Models;
using Escalated.Services.Newsletter;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Escalated.Tests.Services.Newsletter;

public class NewsletterPermissionSeederTests
{
    /// <summary>
    /// The seeder attached the permissions to a role with the slug <c>admin</c>, which
    /// nothing in the package creates. The admin role is <c>escalated-admin</c>.
    /// </summary>
    [Fact]
    public async Task SeedAsync_AttachesBothPermissionsToTheEscalatedAdminRole_Once()
    {
        using var db = TestHelpers.CreateDb();
        var admin = new Role { Name = "Escalated Admin", Slug = AdminUsersController.AdminRoleSlug, IsSystem = true };
        db.Roles.Add(admin);
        await db.SaveChangesAsync();

        await new NewsletterPermissionSeeder(db).SeedAsync();
        await new NewsletterPermissionSeeder(db).SeedAsync();

        var slugs = await db.RolePermissions
            .Where(rp => rp.RoleId == admin.Id)
            .Select(rp => rp.Permission!.Slug)
            .OrderBy(slug => slug)
            .ToListAsync();

        Assert.Equal(new[] { "newsletters.manage", "newsletters.send" }, slugs);
    }
}
