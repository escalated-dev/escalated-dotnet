using Escalated.Configuration;
using Escalated.Data;
using Escalated.Models.Newsletter;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Escalated.Services.Newsletter;

public class NewsletterDispatcher
{
    private static readonly int[] BackoffMinutes = [1, 5, 30];

    private readonly EscalatedDbContext _db;
    private readonly NewsletterRenderer _renderer;
    private readonly INewsletterEmailSender _sender;
    private readonly INewsletterRateLimitStore _rateLimit;
    private readonly INewsletterClock _clock;
    private readonly IOptions<EscalatedOptions> _options;
    private readonly ILogger<NewsletterDispatcher> _logger;

    public NewsletterDispatcher(
        EscalatedDbContext db,
        NewsletterRenderer renderer,
        INewsletterEmailSender sender,
        INewsletterRateLimitStore rateLimit,
        INewsletterClock clock,
        IOptions<EscalatedOptions> options,
        ILogger<NewsletterDispatcher> logger)
    {
        _db = db;
        _renderer = renderer;
        _sender = sender;
        _rateLimit = rateLimit;
        _clock = clock;
        _options = options;
        _logger = logger;
    }

    public async Task DispatchBatchAsync(CancellationToken ct = default)
    {
        if (!_options.Value.EnableNewsletters)
            return;

        await ReclaimStuckRowsAsync(ct);

        var batchSize = Math.Max(1, _options.Value.Newsletters.BatchSize);
        var rateLimit = Math.Max(1, _options.Value.Newsletters.RateLimitPerMinute);
        var minuteKey = $"escalated:newsletters:sent:{_clock.UtcNow:yyyyMMddHHmm}";
        var sentThisMinute = _rateLimit.Get(minuteKey);
        var allowance = Math.Max(0, rateLimit - sentThisMinute);

        if (allowance > 0)
        {
            var claimLimit = Math.Min(batchSize, allowance);
            var now = _clock.UtcNow;
            var pending = await _db.NewsletterDeliveries
                .Where(d => d.Status == "pending" && (d.NextAttemptAt == null || d.NextAttemptAt <= now))
                .OrderBy(d => d.Id)
                .Take(claimLimit)
                .ToListAsync(ct);

            if (pending.Count > 0)
            {
                foreach (var delivery in pending)
                {
                    delivery.Status = "queued";
                    delivery.ClaimedAt = now;
                }

                await _db.SaveChangesAsync(ct);
                _rateLimit.Put(minuteKey, sentThisMinute + pending.Count, now.AddMinutes(2));

                foreach (var delivery in pending)
                {
                    await DispatchOneAsync(delivery.Id, ct);
                }
            }
        }

        await FinalizeCompletedNewslettersAsync(ct);
        await CheckAutoPauseAcrossActiveNewslettersAsync(ct);
    }

    private async Task DispatchOneAsync(long deliveryId, CancellationToken ct)
    {
        var delivery = await _db.NewsletterDeliveries
            .Include(d => d.Newsletter)!.ThenInclude(n => n!.Template)
            .Include(d => d.Contact)
            .SingleOrDefaultAsync(d => d.Id == deliveryId, ct);

        if (delivery?.Newsletter is null || delivery.Contact is null)
            return;

        try
        {
            var html = _renderer.Render(delivery, delivery.Newsletter, delivery.Contact, delivery.Newsletter.Template);
            await _sender.SendAsync(delivery, html, ct);

            delivery.Status = "sent";
            delivery.SentAt = _clock.UtcNow;
            delivery.ClaimedAt = null;
            delivery.NextAttemptAt = null;
            delivery.Newsletter.SummarySent++;
            delivery.Newsletter.UpdatedAt = _clock.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Newsletter delivery {DeliveryId} failed", delivery.Id);
            var attempts = (short)(delivery.AttemptCount + 1);
            delivery.AttemptCount = attempts;
            delivery.ClaimedAt = null;

            if (attempts >= BackoffMinutes.Length)
            {
                delivery.Status = "failed";
                delivery.FailureReason = ex.Message;
                delivery.NextAttemptAt = null;
            }
            else
            {
                delivery.Status = "pending";
                delivery.NextAttemptAt = _clock.UtcNow.AddMinutes(BackoffMinutes[attempts - 1]);
            }

            await _db.SaveChangesAsync(ct);
        }
    }

    private async Task ReclaimStuckRowsAsync(CancellationToken ct)
    {
        var cutoff = _clock.UtcNow.AddMinutes(-Math.Max(1, _options.Value.Newsletters.ClaimTimeoutMinutes));
        var stuck = await _db.NewsletterDeliveries
            .Where(d => d.Status == "queued" && d.ClaimedAt != null && d.ClaimedAt < cutoff)
            .ToListAsync(ct);

        foreach (var delivery in stuck)
        {
            delivery.Status = "pending";
            delivery.ClaimedAt = null;
        }

        if (stuck.Count > 0)
            await _db.SaveChangesAsync(ct);
    }

    private async Task FinalizeCompletedNewslettersAsync(CancellationToken ct)
    {
        var sending = await _db.Newsletters.Where(n => n.Status == "sending").ToListAsync(ct);
        foreach (var newsletter in sending)
        {
            var hasRemaining = await _db.NewsletterDeliveries.AnyAsync(
                d => d.NewsletterId == newsletter.Id && (d.Status == "pending" || d.Status == "queued"),
                ct);

            if (!hasRemaining)
            {
                newsletter.Status = "sent";
                newsletter.SentAt ??= _clock.UtcNow;
                newsletter.UpdatedAt = _clock.UtcNow;
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task CheckAutoPauseAcrossActiveNewslettersAsync(CancellationToken ct)
    {
        var threshold = Math.Max(1, _options.Value.Newsletters.AutoPauseThreshold);
        var rate = _options.Value.Newsletters.AutoPauseBounceRate;
        var sending = await _db.Newsletters.Where(n => n.Status == "sending").ToListAsync(ct);

        foreach (var newsletter in sending)
        {
            var firstTerminal = await _db.NewsletterDeliveries
                .Where(d => d.NewsletterId == newsletter.Id &&
                    (d.Status == "sent" || d.Status == "bounced" || d.Status == "complained" || d.Status == "failed"))
                .OrderBy(d => d.Id)
                .Take(threshold)
                .Select(d => d.Status)
                .ToListAsync(ct);

            if (firstTerminal.Count < threshold)
                continue;

            var bounced = firstTerminal.Count(s => s == "bounced");
            if (bounced / (double)threshold >= rate)
            {
                newsletter.Status = "paused";
                newsletter.UpdatedAt = _clock.UtcNow;
                _logger.LogWarning(
                    "Newsletter {NewsletterId} auto-paused due to bounce rate {Bounced}/{Sampled}",
                    newsletter.Id,
                    bounced,
                    threshold);
            }
        }

        await _db.SaveChangesAsync(ct);
    }
}
