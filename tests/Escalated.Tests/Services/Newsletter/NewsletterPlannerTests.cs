using Escalated.Data;
using Escalated.Models;
using Escalated.Models.Newsletter;
using Escalated.Services.Newsletter;
using Microsoft.EntityFrameworkCore;
using Xunit;
using NewsletterEntity = Escalated.Models.Newsletter.Newsletter;

namespace Escalated.Tests.Services.Newsletter;

public class NewsletterPlannerTests
{
    [Fact]
    public async Task PlanAsync_SnapshotsSendableRecipientsWithUniqueTokensAndSummaryTotal()
    {
        var now = new DateTime(2026, 6, 2, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeNewsletterClock(now);
        var db = TestHelpers.CreateInMemoryDb();

        var list = new NewsletterList { Name = "Main", Kind = "static" };
        db.NewsletterLists.Add(list);
        await db.SaveChangesAsync();

        var sendable = new Contact
        {
            Email = "sendable@example.com",
            Name = "Sendable",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var optedOut = new Contact
        {
            Email = "opted@example.com",
            Name = "Opted out",
            MarketingOptOutAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var suppressed = new Contact
        {
            Email = "Suppressed@Example.com",
            Name = "Suppressed",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Contacts.AddRange(sendable, optedOut, suppressed);
        await db.SaveChangesAsync();

        foreach (var contact in new[] { sendable, optedOut, suppressed })
        {
            db.NewsletterListMembers.Add(new NewsletterListMember
            {
                ListId = list.Id,
                ContactId = contact.Id,
            });
        }

        await db.SaveChangesAsync();
        await new BounceSuppressionStore(db).MarkBouncedAsync(suppressed.Email);

        var newsletter = new NewsletterEntity
        {
            Subject = "Plan test",
            FromEmail = "news@example.com",
            TargetListId = list.Id,
            BodyMarkdown = "Hello",
            Status = "scheduled",
        };
        db.Newsletters.Add(newsletter);
        await db.SaveChangesAsync();

        var planner = new NewsletterPlanner(
            db,
            new ContactSegmentResolver(db),
            new BounceSuppressionStore(db),
            clock);

        await planner.PlanAsync(newsletter);

        var tracked = await db.Newsletters.SingleAsync(n => n.Id == newsletter.Id);
        Assert.Equal("sending", tracked.Status);
        Assert.Equal(1, tracked.SummaryTotal);

        var deliveries = await db.NewsletterDeliveries
            .Where(d => d.NewsletterId == newsletter.Id)
            .ToListAsync();

        Assert.Single(deliveries);
        var delivery = deliveries[0];
        Assert.Equal(sendable.Id, delivery.ContactId);
        Assert.Equal("pending", delivery.Status);
        Assert.Equal(sendable.Email, delivery.EmailAtSend);
        Assert.False(string.IsNullOrWhiteSpace(delivery.TrackingToken));
        Assert.Equal(40, delivery.TrackingToken.Length);
        Assert.Equal(deliveries.Select(d => d.TrackingToken).Distinct().Count(), deliveries.Count);
    }
}
