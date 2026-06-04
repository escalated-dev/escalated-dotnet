using System.Text;
using System.Text.Json;
using Escalated.Configuration;
using Escalated.Controllers.Newsletter;
using Escalated.Data;
using Escalated.Models;
using Escalated.Services.Newsletter;
using Escalated.Tests.Services.Newsletter;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Escalated.Tests.Controllers;

public class NewsletterControllerTests
{
    [Fact]
    public async Task Open_ReturnsGif_AndRecordsOpen()
    {
        var db = TestHelpers.CreateInMemoryDb();
        var token = await SeedDeliveryAsync(db);
        var tracker = CreateTracker(db);
        var controller = CreatePublicController(db, tracker);

        var result = await controller.Open(token + ".gif", CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("image/gif", file.ContentType);
        Assert.NotEmpty(file.FileContents);

        var delivery = await db.NewsletterDeliveries.SingleAsync(d => d.TrackingToken == token);
        Assert.NotNull(delivery.OpenedAt);
    }

    [Fact]
    public async Task Click_Redirects_AndRecordsClick()
    {
        var db = TestHelpers.CreateInMemoryDb();
        var token = await SeedDeliveryAsync(db);
        var tracker = CreateTracker(db);
        var controller = CreatePublicController(db, tracker);
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("https://example.com/page"))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        var result = await controller.Click(token, encoded, CancellationToken.None);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("https://example.com/page", redirect.Url);

        var delivery = await db.NewsletterDeliveries.SingleAsync(d => d.TrackingToken == token);
        Assert.Equal(1, delivery.ClicksCount);
    }

    [Fact]
    public async Task Click_InvalidUrl_RedirectsHome()
    {
        var db = TestHelpers.CreateInMemoryDb();
        var token = await SeedDeliveryAsync(db);
        var controller = CreatePublicController(db, CreateTracker(db));
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("javascript:alert(1)"))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        var result = await controller.Click(token, encoded, CancellationToken.None);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/", redirect.Url);
    }

    [Fact]
    public async Task UnsubscribeStore_SetsMarketingOptOut()
    {
        var db = TestHelpers.CreateInMemoryDb();
        var token = await SeedDeliveryAsync(db);
        var controller = CreatePublicController(db, CreateTracker(db));

        var result = await controller.UnsubscribeStore(token, CancellationToken.None);

        Assert.IsType<ContentResult>(result);
        var contact = await db.Contacts.SingleAsync();
        Assert.NotNull(contact.MarketingOptOutAt);
    }

    [Fact]
    public async Task SendgridWebhook_MapsSpamReportToComplaint()
    {
        var db = TestHelpers.CreateInMemoryDb();
        await NewsletterTestHelpers.SeedNewsletterGraphAsync(db);
        var delivery = await db.NewsletterDeliveries
            .Include(d => d.Newsletter)
            .SingleAsync();
        delivery.TrackingToken = "abc123def456";
        await db.SaveChangesAsync();
        var controller = CreateWebhookController(db);
        var payload =
            $$"""[{"event":"spamreport","smtp-id":"<n-{{delivery.NewsletterId}}-{{delivery.TrackingToken}}@example.com>"}]""";
        var body = JsonDocument.Parse(payload).RootElement;

        var result = await controller.Sendgrid(body, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        await db.Entry(delivery).ReloadAsync();
        Assert.Equal("complained", delivery.Status);
    }

    [Fact]
    public async Task ListStore_AndShow_RoundTrip()
    {
        var db = TestHelpers.CreateInMemoryDb();
        await SeedAdminAsync(db);
        var controller = CreateListController(db);
        var body = JsonDocument.Parse("""{"name":"VIP","kind":"static"}""").RootElement;

        await controller.Store(body, CancellationToken.None);
        var listId = await db.NewsletterLists.Select(l => l.Id).SingleAsync();

        var show = await controller.Show(listId, CancellationToken.None);
        var inertia = Assert.IsType<OkObjectResult>(show);
        Assert.Contains("Lists/Show", inertia.Value?.ToString());
    }

    [Fact]
    public async Task TemplateStore_RoundTripsIndex()
    {
        var db = TestHelpers.CreateInMemoryDb();
        await SeedAdminAsync(db);
        var controller = CreateTemplateController(db);
        var body = JsonDocument.Parse("""{"name":"Welcome","theme":"default","body_markdown":"Hello"}""").RootElement;

        await controller.Store(body, CancellationToken.None);

        var index = await controller.Index(CancellationToken.None);
        Assert.IsType<OkObjectResult>(index);
        Assert.Equal(1, await db.NewsletterTemplates.CountAsync());
    }

    [Fact]
    public async Task NewsletterEnabledFilter_ReturnsNotFoundWhenDisabled()
    {
        var filter = new NewsletterEnabledFilter(Options.Create(new EscalatedOptions { EnableNewsletters = false }));
        var context = new ActionContext
        {
            HttpContext = new DefaultHttpContext(),
            RouteData = new RouteData(),
            ActionDescriptor = new ActionDescriptor(),
        };
        var executing = new ActionExecutingContext(
            context,
            new List<IFilterMetadata>(),
            new Dictionary<string, object?>(),
            controller: null!);

        await filter.OnActionExecutionAsync(executing, () => throw new InvalidOperationException("should not run"));

        Assert.IsType<NotFoundResult>(executing.Result);
    }

    private static NewsletterTracker CreateTracker(EscalatedDbContext db) =>
        new(db, new BounceSuppressionStore(db), new FakeNewsletterClock(DateTime.UtcNow));

    private static NewsletterPublicController CreatePublicController(EscalatedDbContext db, NewsletterTracker tracker) =>
        new(tracker, NewsletterTestHelpers.CreateRenderer(), db)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    private static NewsletterEspWebhookController CreateWebhookController(EscalatedDbContext db) =>
        new(CreateTracker(db), db)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    private static AdminNewsletterListController CreateListController(EscalatedDbContext db) =>
        new(db, new NewsletterPermissionService(db), new ContactSegmentResolver(db))
        {
            ControllerContext = CreateAdminContext(),
        };

    private static AdminNewsletterTemplateController CreateTemplateController(EscalatedDbContext db) =>
        new(db, new NewsletterPermissionService(db), NewsletterTestHelpers.NewsletterOptions())
        {
            ControllerContext = CreateAdminContext(),
        };

    private static ControllerContext CreateAdminContext() => new()
    {
        HttpContext = new DefaultHttpContext
        {
            User = new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity([new("sub", "admin-user")], "test")),
        },
    };

    private static async Task<string> SeedDeliveryAsync(EscalatedDbContext db)
    {
        await NewsletterTestHelpers.SeedNewsletterGraphAsync(db);
        return await db.NewsletterDeliveries.Select(d => d.TrackingToken).SingleAsync();
    }

    private static async Task SeedAdminAsync(EscalatedDbContext db)
    {
        var role = new Role { Name = "Admin", Slug = "admin" };
        db.Roles.Add(role);
        var permission = new Permission { Slug = "newsletters.manage", Name = "Manage" };
        db.Permissions.Add(permission);
        await db.SaveChangesAsync();
        db.RoleUsers.Add(new RoleUser { RoleId = role.Id, UserId = "admin-user" });
        db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id });
        await db.SaveChangesAsync();
    }
}
