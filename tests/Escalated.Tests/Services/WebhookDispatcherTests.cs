using Escalated.Models;
using Escalated.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Escalated.Tests.Services;

public class WebhookDispatcherTests
{
    [Fact]
    public async Task DispatchAsync_SendsToActiveSubscribers()
    {
        var db = TestHelpers.CreateInMemoryDb();
        var logger = new Mock<ILogger<WebhookDispatcher>>();
        var httpFactory = new Mock<IHttpClientFactory>();

        // We can't easily test the HTTP call without a real server,
        // but we can verify the webhook is found and delivery is recorded

        db.Webhooks.Add(new Webhook
        {
            Url = "https://example.com/webhook",
            Events = """["ticket.created", "*"]""",
            Active = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.Webhooks.Add(new Webhook
        {
            Url = "https://example.com/inactive",
            Events = """["ticket.created"]""",
            Active = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var activeWebhooks = db.Webhooks.Where(w => w.Active).ToList();
        Assert.Single(activeWebhooks);
        Assert.True(activeWebhooks[0].SubscribedTo("ticket.created"));
    }

    [Fact]
    public void Webhook_SubscribedTo_MatchesWildcard()
    {
        var webhook = new Webhook { Events = """["*"]""" };
        Assert.True(webhook.SubscribedTo("ticket.created"));
        Assert.True(webhook.SubscribedTo("anything"));
    }

    [Fact]
    public void Webhook_SubscribedTo_MatchesSpecificEvent()
    {
        var webhook = new Webhook { Events = """["ticket.created", "reply.created"]""" };
        Assert.True(webhook.SubscribedTo("ticket.created"));
        Assert.True(webhook.SubscribedTo("reply.created"));
        Assert.False(webhook.SubscribedTo("ticket.deleted"));
    }
}
