using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Escalated.Data;
using Escalated.Models;
using Escalated.Services.Newsletter;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NewsletterDelivery = Escalated.Models.Newsletter.NewsletterDelivery;

namespace Escalated.Controllers.Newsletter;

[ApiController]
[NewsletterEnabled]
public class NewsletterPublicController : ControllerBase
{
    private static readonly byte[] TransparentGif =
    {
        0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x01, 0x00, 0x01, 0x00, 0x80, 0x00, 0x00,
        0xff, 0xff, 0xff, 0x00, 0x00, 0x00, 0x21, 0xf9, 0x04, 0x01, 0x00, 0x00, 0x00,
        0x00, 0x2c, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x02, 0x02,
        0x44, 0x01, 0x00, 0x3b,
    };

    private static readonly ConcurrentDictionary<string, (int Count, long ExpiresAt)> UnsubscribeAttempts = new();

    private static readonly Regex TokenExtensionRegex = new(@"\.(gif|png|jpg)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly NewsletterTracker _tracker;
    private readonly NewsletterRenderer _renderer;
    private readonly EscalatedDbContext _db;

    public NewsletterPublicController(
        NewsletterTracker tracker,
        NewsletterRenderer renderer,
        EscalatedDbContext db)
    {
        _tracker = tracker;
        _renderer = renderer;
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Open(string token, CancellationToken ct)
    {
        token = StripTokenExtension(token);
        await _tracker.RecordOpenAsync(token, ct);
        return File(TransparentGif, "image/gif");
    }

    [HttpGet]
    public async Task<IActionResult> Click(string token, [FromQuery] string? u, CancellationToken ct)
    {
        string destination;
        try
        {
            destination = NewsletterHttp.DecodeTrackedUrl(u ?? string.Empty);
        }
        catch
        {
            return Redirect("/");
        }

        await _tracker.RecordClickAsync(token, destination, ct);
        return Redirect(destination);
    }

    [HttpGet]
    public async Task<IActionResult> UnsubscribeShow(string token, CancellationToken ct)
    {
        var delivery = await FindDeliveryAsync(token, ct);
        return Content(
            UnsubscribeHtml(token, delivery?.EmailAtSend, confirmed: false),
            "text/html");
    }

    [HttpPost]
    public async Task<IActionResult> UnsubscribeStore(string token, CancellationToken ct)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (TooManyUnsubscribeAttempts(ip))
            return StatusCode(429, "Too Many Requests");

        var delivery = await FindDeliveryAsync(token, ct);
        if (delivery?.ContactId is int contactId)
        {
            var contact = await _db.Contacts.SingleOrDefaultAsync(c => c.Id == contactId, ct);
            if (contact is not null)
            {
                contact.MarketingOptOutAt = DateTime.UtcNow;
                contact.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
            }
        }

        return Content(
            UnsubscribeHtml(token, delivery?.EmailAtSend, confirmed: true),
            "text/html");
    }

    [HttpGet]
    public async Task<IActionResult> View(string token, CancellationToken ct)
    {
        var delivery = await _db.NewsletterDeliveries
            .Include(d => d.Newsletter)!.ThenInclude(n => n!.Template)
            .Include(d => d.Contact)
            .SingleOrDefaultAsync(d => d.TrackingToken == token, ct);

        if (delivery?.Newsletter is null || delivery.Contact is null)
        {
            return Content(
                """<!doctype html><html lang="en"><head><meta charset="utf-8"><title>Email unavailable</title></head><body><p>This email is no longer available.</p></body></html>""",
                "text/html");
        }

        var html = _renderer.Render(
            delivery,
            delivery.Newsletter,
            delivery.Contact,
            delivery.Newsletter.Template);
        return Content(html, "text/html");
    }

    private Task<NewsletterDelivery?> FindDeliveryAsync(string token, CancellationToken ct) =>
        _db.NewsletterDeliveries.SingleOrDefaultAsync(d => d.TrackingToken == token, ct);

    private static string StripTokenExtension(string token) =>
        TokenExtensionRegex.Replace(token, string.Empty);

    private static bool TooManyUnsubscribeAttempts(string ip)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var entry = UnsubscribeAttempts.AddOrUpdate(
            ip,
            _ => (1, now + 60_000),
            (_, existing) =>
            {
                if (existing.ExpiresAt <= now)
                    return (1, now + 60_000);
                return (existing.Count + 1, existing.ExpiresAt);
            });
        return entry.Count > 60;
    }

    private static string UnsubscribeHtml(string token, string? email, bool confirmed)
    {
        var escapedToken = NewsletterHttp.Escape(token);
        var escapedEmail = NewsletterHttp.Escape(email ?? string.Empty);
        var message = confirmed
            ? "You have been unsubscribed."
            : "Confirm that you want to unsubscribe from marketing emails.";
        return $"""<!doctype html><html lang="en"><head><meta charset="utf-8"><title>Unsubscribe</title></head><body><main><h1>Unsubscribe</h1><p>{message}</p><p>{escapedEmail}</p><form method="post" action="/escalated/n/u/{escapedToken}"><button type="submit">Unsubscribe</button></form></main></body></html>""";
    }
}
