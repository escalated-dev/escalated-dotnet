using Escalated.Data;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Services.Newsletter;

public class NewsletterTracker
{
    private readonly EscalatedDbContext _db;
    private readonly BounceSuppressionStore _bounces;
    private readonly INewsletterClock _clock;

    public NewsletterTracker(EscalatedDbContext db, BounceSuppressionStore bounces, INewsletterClock clock)
    {
        _db = db;
        _bounces = bounces;
        _clock = clock;
    }

    public async Task RecordOpenAsync(string token, CancellationToken ct = default)
    {
        try
        {
            var delivery = await FindByTokenAsync(token, ct);
            if (delivery is null || IsTerminalNegative(delivery.Status))
                return;

            if (delivery.OpenedAt is null)
            {
                delivery.OpenedAt = _clock.UtcNow;
                if (delivery.Newsletter is not null)
                    delivery.Newsletter.SummaryOpened++;
                await _db.SaveChangesAsync(ct);
            }
        }
        catch
        {
            // Tracking is fire-and-forget.
        }
    }

    public async Task RecordClickAsync(string token, string url, CancellationToken ct = default)
    {
        try
        {
            var delivery = await FindByTokenAsync(token, ct);
            if (delivery is null || IsTerminalNegative(delivery.Status))
                return;

            var firstClick = delivery.ClicksCount == 0;
            delivery.ClicksCount++;
            delivery.LastClickedAt = _clock.UtcNow;

            if (delivery.OpenedAt is null)
            {
                delivery.OpenedAt = _clock.UtcNow;
                if (delivery.Newsletter is not null)
                    delivery.Newsletter.SummaryOpened++;
            }

            if (firstClick && delivery.Newsletter is not null)
                delivery.Newsletter.SummaryClicked++;

            await _db.SaveChangesAsync(ct);
        }
        catch
        {
            // Tracking is fire-and-forget.
        }
    }

    public async Task RecordBounceAsync(string token, string type, string? reason = null, CancellationToken ct = default)
    {
        try
        {
            if (type != "hard")
                return;

            var delivery = await FindByTokenAsync(token, ct);
            if (delivery is null || delivery.Status == "bounced")
                return;

            delivery.Status = "bounced";
            delivery.BounceReason = reason;
            if (delivery.Newsletter is not null)
                delivery.Newsletter.SummaryBounced++;
            await _db.SaveChangesAsync(ct);
            await _bounces.MarkBouncedAsync(delivery.EmailAtSend, ct);
        }
        catch
        {
            // Tracking is fire-and-forget.
        }
    }

    public async Task RecordComplaintAsync(string token, CancellationToken ct = default)
    {
        try
        {
            var delivery = await FindByTokenAsync(token, ct);
            if (delivery is null || delivery.Status == "complained")
                return;

            delivery.Status = "complained";
            if (delivery.Newsletter is not null)
                delivery.Newsletter.SummaryComplained++;
            await _db.SaveChangesAsync(ct);
            await _bounces.MarkComplainedAsync(delivery.EmailAtSend, ct);
        }
        catch
        {
            // Tracking is fire-and-forget.
        }
    }

    private Task<Models.Newsletter.NewsletterDelivery?> FindByTokenAsync(string token, CancellationToken ct) =>
        _db.NewsletterDeliveries
            .Include(d => d.Newsletter)
            .SingleOrDefaultAsync(d => d.TrackingToken == token, ct);

    private static bool IsTerminalNegative(string status) => status is "bounced" or "complained" or "failed";
}
