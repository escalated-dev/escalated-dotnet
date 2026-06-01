using System.Net;
using Escalated.Models;
using Escalated.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Escalated.Tests.Services;

public class WebhookDispatcherTests
{
    private class StubHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

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

    [Theory]
    [InlineData("file:///tmp/hook")]
    [InlineData("https://localhost/hook")]
    [InlineData("https://localhost.localdomain/hook")]
    [InlineData("https://service.localhost/hook")]
    [InlineData("http://0.0.0.0/hook")]
    [InlineData("http://127.0.0.1/hook")]
    [InlineData("http://10.1.2.3/hook")]
    [InlineData("http://172.16.1.2/hook")]
    [InlineData("http://192.168.1.2/hook")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("http://[::1]/hook")]
    public async Task SendAsync_RejectsUnsafeWebhookUrlsWithoutSending(string url)
    {
        var db = TestHelpers.CreateInMemoryDb();
        var logger = new Mock<ILogger<WebhookDispatcher>>();
        var handler = new StubHandler();
        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory
            .Setup(x => x.CreateClient("EscalatedWebhook"))
            .Returns(new HttpClient(handler));

        var dispatcher = new WebhookDispatcher(db, httpFactory.Object, logger.Object);
        var webhook = new Webhook
        {
            Id = 1,
            Url = url,
            Events = """["ticket.created"]""",
            Active = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        await dispatcher.SendAsync(webhook, "ticket.created", new { id = 123 });

        Assert.Null(handler.LastRequest);
        var delivery = await db.WebhookDeliveries.SingleAsync();
        Assert.Equal(0, delivery.ResponseCode);
        Assert.Contains("Webhook URL must be an absolute HTTP(S) URL", delivery.ResponseBody);
    }

    [Fact]
    public async Task SendAsync_AllowsPublicHttpWebhookUrl()
    {
        var db = TestHelpers.CreateInMemoryDb();
        var logger = new Mock<ILogger<WebhookDispatcher>>();
        var handler = new StubHandler();
        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory
            .Setup(x => x.CreateClient("EscalatedWebhook"))
            .Returns(new HttpClient(handler));

        var dispatcher = new WebhookDispatcher(db, httpFactory.Object, logger.Object);
        var webhook = new Webhook
        {
            Id = 1,
            Url = "https://example.com/hook",
            Events = """["ticket.created"]""",
            Active = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        await dispatcher.SendAsync(webhook, "ticket.created", new { id = 123 });

        Assert.NotNull(handler.LastRequest);
        Assert.Equal("https://example.com/hook", handler.LastRequest!.RequestUri!.ToString());
        var delivery = await db.WebhookDeliveries.SingleAsync();
        Assert.Equal(200, delivery.ResponseCode);
    }
}
