using Escalated.Configuration;
using Escalated.Data;
using Escalated.Models;
using Escalated.Models.Newsletter;
using Escalated.Services.Newsletter;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NewsletterEntity = Escalated.Models.Newsletter.Newsletter;

namespace Escalated.Tests.Services.Newsletter;

internal sealed class FakeNewsletterClock : INewsletterClock
{
    public FakeNewsletterClock(DateTime utcNow) => UtcNow = utcNow;

    public DateTime UtcNow { get; set; }
}

internal sealed class RecordingNewsletterEmailSender : INewsletterEmailSender
{
    public List<long> SentDeliveryIds { get; } = new();

    public Task SendAsync(NewsletterDelivery delivery, string html, CancellationToken ct = default)
    {
        SentDeliveryIds.Add(delivery.Id);
        return Task.CompletedTask;
    }
}

internal sealed class FailingNewsletterEmailSender : INewsletterEmailSender
{
    public Task SendAsync(NewsletterDelivery delivery, string html, CancellationToken ct = default) =>
        throw new InvalidOperationException("simulated send failure");
}

/// <summary>
/// In-memory rate limit store without real-clock expiry (production store uses DateTime.UtcNow).
/// </summary>
internal sealed class TestNewsletterRateLimitStore : INewsletterRateLimitStore
{
    private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);

    public int Get(string key) => _counts.GetValueOrDefault(key);

    public void Put(string key, int value, DateTime expiresAtUtc) => _counts[key] = value;
}

internal static class NewsletterTestHelpers
{
    internal static string ThemesDir()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "Escalated", "Views", "NewsletterThemes"));
        return path;
    }

    internal static NewsletterRenderer CreateRenderer() =>
        new(new NewsletterRendererOptions
        {
            BaseUrl = "http://localhost",
            ThemesDir = ThemesDir(),
            TrackingEnabled = false,
        });

    internal static IOptions<EscalatedOptions> NewsletterOptions(Action<NewsletterOptions>? configure = null)
    {
        var options = new EscalatedOptions
        {
            EnableNewsletters = true,
            Newsletters = new NewsletterOptions
            {
                RateLimitPerMinute = 60,
                BatchSize = 50,
                ClaimTimeoutMinutes = 10,
                AutoPauseThreshold = 100,
                AutoPauseBounceRate = 0.05,
                ThemesDir = ThemesDir(),
            },
        };
        configure?.Invoke(options.Newsletters);
        return Options.Create(options);
    }

    internal static NewsletterDispatcher CreateDispatcher(
        EscalatedDbContext db,
        INewsletterClock clock,
        INewsletterEmailSender sender,
        INewsletterRateLimitStore? rateLimit = null,
        Action<NewsletterOptions>? configure = null) =>
        new(
            db,
            CreateRenderer(),
            sender,
            rateLimit ?? new MemoryNewsletterRateLimitStore(),
            clock,
            NewsletterOptions(configure),
            NullLogger<NewsletterDispatcher>.Instance);

    internal static async Task<(NewsletterEntity Newsletter, NewsletterList List, Contact Contact)> SeedNewsletterGraphAsync(
        EscalatedDbContext db,
        string deliveryStatus = "pending",
        DateTime? claimedAt = null,
        DateTime? nextAttemptAt = null,
        short attemptCount = 0)
    {
        var list = new NewsletterList { Name = "Test list", Kind = "static" };
        var template = new NewsletterTemplate
        {
            Name = "Default",
            Theme = "default",
            BodyMarkdown = "Hello",
        };
        db.NewsletterLists.Add(list);
        db.NewsletterTemplates.Add(template);
        await db.SaveChangesAsync();

        var contact = new Contact
        {
            Email = "recipient@example.com",
            Name = "Recipient",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Contacts.Add(contact);
        await db.SaveChangesAsync();

        db.NewsletterListMembers.Add(new NewsletterListMember
        {
            ListId = list.Id,
            ContactId = contact.Id,
        });

        var newsletter = new NewsletterEntity
        {
            Subject = "Test",
            FromEmail = "news@example.com",
            TargetListId = list.Id,
            TemplateId = template.Id,
            BodyMarkdown = "Hi",
            Status = "sending",
            SummaryTotal = 1,
        };
        db.Newsletters.Add(newsletter);
        await db.SaveChangesAsync();

        var delivery = new NewsletterDelivery
        {
            NewsletterId = newsletter.Id,
            ContactId = contact.Id,
            EmailAtSend = contact.Email,
            Status = deliveryStatus,
            TrackingToken = "tok-" + Guid.NewGuid().ToString("N")[..8],
            AttemptCount = attemptCount,
            ClaimedAt = claimedAt,
            NextAttemptAt = nextAttemptAt,
        };
        db.NewsletterDeliveries.Add(delivery);
        await db.SaveChangesAsync();

        return (newsletter, list, contact);
    }
}
