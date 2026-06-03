using Escalated.Data;
using Escalated.Models;
using Escalated.Models.Newsletter;
using Escalated.Services.Newsletter;
using Xunit;

namespace Escalated.Tests.Services.Newsletter;

public class ContactSegmentResolverTests
{
    [Fact]
    public async Task ResolveSendableAsync_StaticList_ExcludesOptedOutContacts()
    {
        var db = TestHelpers.CreateInMemoryDb();
        var resolver = new ContactSegmentResolver(db);

        var list = new NewsletterList { Name = "Static", Kind = "static" };
        db.NewsletterLists.Add(list);
        await db.SaveChangesAsync();

        var sendable = new Contact
        {
            Email = "yes@example.com",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        var optedOut = new Contact
        {
            Email = "no@example.com",
            MarketingOptOutAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Contacts.AddRange(sendable, optedOut);
        await db.SaveChangesAsync();

        db.NewsletterListMembers.AddRange(
            new NewsletterListMember { ListId = list.Id, ContactId = sendable.Id },
            new NewsletterListMember { ListId = list.Id, ContactId = optedOut.Id });
        await db.SaveChangesAsync();

        var all = await resolver.ResolveAsync(list);
        var filtered = await resolver.ResolveSendableAsync(list);

        Assert.Equal(new[] { sendable.Id, optedOut.Id }, all.OrderBy(id => id));
        Assert.Equal(new[] { sendable.Id }, filtered);
    }

    [Fact]
    public async Task ResolveSendableAsync_DynamicFilter_RejectsUnknownFieldAndOperator()
    {
        var db = TestHelpers.CreateInMemoryDb();
        var resolver = new ContactSegmentResolver(db);

        var target = new Contact
        {
            Email = "victim@example.com",
            Name = "Victim",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        var other = new Contact
        {
            Email = "safe@example.com",
            Name = "Safe",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Contacts.AddRange(target, other);
        await db.SaveChangesAsync();

        var list = new NewsletterList
        {
            Name = "Dynamic",
            Kind = "dynamic",
            FilterJson = """
                {
                  "rules": [
                    { "field": "'; DROP TABLE contacts; --", "op": "=", "value": "Victim" },
                    { "field": "email", "op": "exec", "value": "victim@example.com" },
                    { "field": "email", "op": "contains", "value": "victim" }
                  ]
                }
                """,
        };
        db.NewsletterLists.Add(list);
        await db.SaveChangesAsync();

        var ids = await resolver.ResolveSendableAsync(list);

        Assert.Equal(new[] { target.Id }, ids);
    }
}
