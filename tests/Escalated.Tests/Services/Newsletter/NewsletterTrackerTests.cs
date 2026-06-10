using Escalated.Data;
using Escalated.Models;
using Escalated.Models.Newsletter;
using Escalated.Services.Newsletter;
using Microsoft.EntityFrameworkCore;
using Xunit;
using NewsletterEntity = Escalated.Models.Newsletter.Newsletter;

namespace Escalated.Tests.Services.Newsletter;

public class NewsletterTrackerTests
{
    [Fact]
    public async Task RecordOpenAsync_IncrementsSummaryOnce()
    {
        var now = new DateTime(2026, 6, 2, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeNewsletterClock(now);
        var db = TestHelpers.CreateInMemoryDb();
        var tracker = CreateTracker(db, clock);
        var token = await SeedDeliveryAsync(db, "sent");

        await tracker.RecordOpenAsync(token);
        await tracker.RecordOpenAsync(token);

        var delivery = await db.NewsletterDeliveries.SingleAsync(d => d.TrackingToken == token);
        var newsletter = await db.Newsletters.SingleAsync();
        Assert.Equal(now, delivery.OpenedAt);
        Assert.Equal(1, newsletter.SummaryOpened);
    }

    [Fact]
    public async Task RecordClickAsync_IncrementsClicksAndOpenedSummary()
    {
        var now = new DateTime(2026, 6, 2, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeNewsletterClock(now);
        var db = TestHelpers.CreateInMemoryDb();
        var tracker = CreateTracker(db, clock);
        var token = await SeedDeliveryAsync(db, "sent");

        await tracker.RecordClickAsync(token, "https://example.com/docs");

        var delivery = await db.NewsletterDeliveries.SingleAsync(d => d.TrackingToken == token);
        var newsletter = await db.Newsletters.SingleAsync();
        Assert.Equal(1, delivery.ClicksCount);
        Assert.Equal(now, delivery.LastClickedAt);
        Assert.Equal(now, delivery.OpenedAt);
        Assert.Equal(1, newsletter.SummaryOpened);
        Assert.Equal(1, newsletter.SummaryClicked);
    }

    [Fact]
    public async Task RecordBounceAsync_HardBounceSuppressesEmail()
    {
        var now = new DateTime(2026, 6, 2, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeNewsletterClock(now);
        var db = TestHelpers.CreateInMemoryDb();
        var bounces = new BounceSuppressionStore(db);
        var tracker = new NewsletterTracker(db, bounces, clock);
        var token = await SeedDeliveryAsync(db, "sent", email: "Bounce@Example.com");

        await tracker.RecordBounceAsync(token, "hard", "mailbox unavailable");
        await tracker.RecordBounceAsync(token, "soft", "temporary");

        var delivery = await db.NewsletterDeliveries.SingleAsync(d => d.TrackingToken == token);
        var newsletter = await db.Newsletters.SingleAsync();
        Assert.Equal("bounced", delivery.Status);
        Assert.Equal("mailbox unavailable", delivery.BounceReason);
        Assert.Equal(1, newsletter.SummaryBounced);
        Assert.True(await bounces.IsBouncedAsync("bounce@example.com"));
    }

    [Fact]
    public async Task RecordComplaintAsync_UpdatesDeliveryAndSummary()
    {
        var now = new DateTime(2026, 6, 2, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeNewsletterClock(now);
        var db = TestHelpers.CreateInMemoryDb();
        var tracker = CreateTracker(db, clock);
        var token = await SeedDeliveryAsync(db, "sent");

        await tracker.RecordComplaintAsync(token);

        var delivery = await db.NewsletterDeliveries.SingleAsync(d => d.TrackingToken == token);
        var newsletter = await db.Newsletters.SingleAsync();
        Assert.Equal("complained", delivery.Status);
        Assert.Equal(1, newsletter.SummaryComplained);
        Assert.True(await new BounceSuppressionStore(db).IsBouncedAsync(delivery.EmailAtSend));
    }

    [Fact]
    public async Task RecordOpenAsync_UnknownTokenIsNoOp()
    {
        var db = TestHelpers.CreateInMemoryDb();
        var tracker = CreateTracker(db, new FakeNewsletterClock(DateTime.UtcNow));

        await tracker.RecordOpenAsync("does-not-exist");

        Assert.Empty(await db.NewsletterDeliveries.ToListAsync());
    }

    private static NewsletterTracker CreateTracker(EscalatedDbContext db, INewsletterClock clock) =>
        new(db, new BounceSuppressionStore(db), clock);

    private static async Task<string> SeedDeliveryAsync(
        EscalatedDbContext db,
        string status,
        string email = "track@example.com")
    {
        var contact = new Contact
        {
            Email = email,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Contacts.Add(contact);
        await db.SaveChangesAsync();

        var list = new NewsletterList { Name = "L", Kind = "static" };
        db.NewsletterLists.Add(list);
        await db.SaveChangesAsync();

        var newsletter = new NewsletterEntity
        {
            Subject = "Track",
            FromEmail = "news@example.com",
            TargetListId = list.Id,
            BodyMarkdown = "Hi",
            Status = "sending",
        };
        db.Newsletters.Add(newsletter);
        await db.SaveChangesAsync();

        var token = "track-token-" + Guid.NewGuid().ToString("N")[..6];
        db.NewsletterDeliveries.Add(new NewsletterDelivery
        {
            NewsletterId = newsletter.Id,
            ContactId = contact.Id,
            EmailAtSend = email,
            Status = status,
            TrackingToken = token,
        });
        await db.SaveChangesAsync();
        return token;
    }
}
