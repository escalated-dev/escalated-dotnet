using Escalated.Data;
using Escalated.Models;
using Escalated.Models.Newsletter;
using Escalated.Services.Newsletter;
using Microsoft.EntityFrameworkCore;
using Xunit;
using NewsletterEntity = Escalated.Models.Newsletter.Newsletter;

namespace Escalated.Tests.Services.Newsletter;

public class NewsletterDispatcherTests
{
    [Fact]
    public async Task DispatchBatchAsync_EnforcesRateLimitPerMinute()
    {
        var now = new DateTime(2026, 6, 2, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeNewsletterClock(now);
        var db = TestHelpers.CreateInMemoryDb();
        var sender = new RecordingNewsletterEmailSender();
        var rateLimit = new TestNewsletterRateLimitStore();
        var dispatcher = NewsletterTestHelpers.CreateDispatcher(
            db, clock, sender, rateLimit, configure: o => o.RateLimitPerMinute = 2);

        await SeedPendingDeliveriesAsync(db, count: 5);

        await dispatcher.DispatchBatchAsync();

        Assert.Equal(2, sender.SentDeliveryIds.Count);
        Assert.Equal(2, await db.NewsletterDeliveries.CountAsync(d => d.Status == "sent"));
        Assert.Equal(3, await db.NewsletterDeliveries.CountAsync(d => d.Status == "pending"));

        await dispatcher.DispatchBatchAsync();

        Assert.Equal(2, sender.SentDeliveryIds.Count);
        Assert.Equal(3, await db.NewsletterDeliveries.CountAsync(d => d.Status == "pending"));
    }

    [Fact]
    public async Task DispatchBatchAsync_SchedulesRetryBackoffViaNextAttemptAt()
    {
        var now = new DateTime(2026, 6, 2, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeNewsletterClock(now);
        var db = TestHelpers.CreateInMemoryDb();
        var dispatcher = NewsletterTestHelpers.CreateDispatcher(db, clock, new FailingNewsletterEmailSender());

        var (_, _, _) = await NewsletterTestHelpers.SeedNewsletterGraphAsync(db);

        await dispatcher.DispatchBatchAsync();

        var delivery = await db.NewsletterDeliveries.SingleAsync();
        Assert.Equal("pending", delivery.Status);
        Assert.Equal(1, delivery.AttemptCount);
        Assert.Equal(now.AddMinutes(1), delivery.NextAttemptAt);
        Assert.Null(delivery.ClaimedAt);

        await dispatcher.DispatchBatchAsync();
        delivery = await db.NewsletterDeliveries.SingleAsync();
        Assert.Equal(1, delivery.AttemptCount);

        clock.UtcNow = now.AddMinutes(1);
        await dispatcher.DispatchBatchAsync();
        delivery = await db.NewsletterDeliveries.SingleAsync();
        Assert.Equal(2, delivery.AttemptCount);
        Assert.Equal(now.AddMinutes(6), delivery.NextAttemptAt);
    }

    [Fact]
    public async Task DispatchBatchAsync_AutoPausesOnFirstNTerminalSampleNotCumulative()
    {
        var now = new DateTime(2026, 6, 2, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeNewsletterClock(now);
        var db = TestHelpers.CreateInMemoryDb();
        var sender = new RecordingNewsletterEmailSender();
        var dispatcher = NewsletterTestHelpers.CreateDispatcher(
            db, clock, sender, configure: o =>
            {
                o.AutoPauseThreshold = 5;
                o.AutoPauseBounceRate = 0.5;
                o.RateLimitPerMinute = 0;
            });

        var newsletter = await SeedAutoPauseScenarioAsync(db);

        await dispatcher.DispatchBatchAsync();

        newsletter = await db.Newsletters.SingleAsync(n => n.Id == newsletter.Id);
        Assert.Equal("paused", newsletter.Status);
    }

    [Fact]
    public async Task DispatchBatchAsync_DoesNotAutoPauseWhenBouncesAreOutsideFirstN()
    {
        var now = new DateTime(2026, 6, 2, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeNewsletterClock(now);
        var db = TestHelpers.CreateInMemoryDb();
        var sender = new RecordingNewsletterEmailSender();
        var dispatcher = NewsletterTestHelpers.CreateDispatcher(
            db, clock, sender, configure: o =>
            {
                o.AutoPauseThreshold = 5;
                o.AutoPauseBounceRate = 0.5;
                o.RateLimitPerMinute = 0;
            });

        var newsletter = await SeedAutoPauseScenarioAsync(db, highBouncesOnlyAfterFirstN: true);

        await dispatcher.DispatchBatchAsync();

        newsletter = await db.Newsletters.SingleAsync(n => n.Id == newsletter.Id);
        Assert.Equal("sending", newsletter.Status);
    }

    [Fact]
    public async Task DispatchBatchAsync_ReclaimsStuckQueuedRows()
    {
        var now = new DateTime(2026, 6, 2, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeNewsletterClock(now);
        var db = TestHelpers.CreateInMemoryDb();
        var sender = new RecordingNewsletterEmailSender();
        var dispatcher = NewsletterTestHelpers.CreateDispatcher(
            db, clock, sender, configure: o => o.ClaimTimeoutMinutes = 10);

        await NewsletterTestHelpers.SeedNewsletterGraphAsync(
            db,
            deliveryStatus: "queued",
            claimedAt: now.AddMinutes(-11));

        await dispatcher.DispatchBatchAsync();

        var delivery = await db.NewsletterDeliveries.SingleAsync();
        Assert.Equal("sent", delivery.Status);
        Assert.Null(delivery.ClaimedAt);
        Assert.Single(sender.SentDeliveryIds);
    }

    private static async Task SeedPendingDeliveriesAsync(EscalatedDbContext db, int count)
    {
        var list = new NewsletterList { Name = "Bulk", Kind = "static" };
        var template = new NewsletterTemplate
        {
            Name = "T",
            Theme = "default",
            BodyMarkdown = "body",
        };
        db.NewsletterLists.Add(list);
        db.NewsletterTemplates.Add(template);
        await db.SaveChangesAsync();

        var newsletter = new NewsletterEntity
        {
            Subject = "Bulk send",
            FromEmail = "news@example.com",
            TargetListId = list.Id,
            TemplateId = template.Id,
            BodyMarkdown = "Hi",
            Status = "sending",
        };
        db.Newsletters.Add(newsletter);
        await db.SaveChangesAsync();

        for (var i = 0; i < count; i++)
        {
            var contact = new Contact
            {
                Email = $"user{i}@example.com",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            db.Contacts.Add(contact);
            await db.SaveChangesAsync();

            db.NewsletterDeliveries.Add(new NewsletterDelivery
            {
                NewsletterId = newsletter.Id,
                ContactId = contact.Id,
                EmailAtSend = contact.Email,
                Status = "pending",
                TrackingToken = $"tok-{i:D3}",
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task<NewsletterEntity> SeedAutoPauseScenarioAsync(
        EscalatedDbContext db,
        bool highBouncesOnlyAfterFirstN = false)
    {
        var list = new NewsletterList { Name = "Pause test", Kind = "static" };
        var template = new NewsletterTemplate
        {
            Name = "T",
            Theme = "default",
            BodyMarkdown = "body",
        };
        db.NewsletterLists.Add(list);
        db.NewsletterTemplates.Add(template);
        await db.SaveChangesAsync();

        var newsletter = new NewsletterEntity
        {
            Subject = "Pause test",
            FromEmail = "news@example.com",
            TargetListId = list.Id,
            TemplateId = template.Id,
            BodyMarkdown = "Hi",
            Status = "sending",
        };
        db.Newsletters.Add(newsletter);
        await db.SaveChangesAsync();

        for (var i = 0; i < 10; i++)
        {
            var contact = new Contact
            {
                Email = $"pause{i}@example.com",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            db.Contacts.Add(contact);
            await db.SaveChangesAsync();

            var status = i switch
            {
                _ when highBouncesOnlyAfterFirstN && i == 0 => "bounced",
                _ when highBouncesOnlyAfterFirstN && i < 5 => "sent",
                _ when highBouncesOnlyAfterFirstN => "pending",
                _ when i < 3 => "bounced",
                _ when i < 5 => "sent",
                _ => "pending",
            };

            db.NewsletterDeliveries.Add(new NewsletterDelivery
            {
                NewsletterId = newsletter.Id,
                ContactId = contact.Id,
                EmailAtSend = contact.Email,
                Status = status,
                TrackingToken = $"pause-{i:D3}",
                SentAt = status is "sent" or "bounced" ? DateTime.UtcNow : null,
            });
        }

        await db.SaveChangesAsync();
        return newsletter;
    }
}
