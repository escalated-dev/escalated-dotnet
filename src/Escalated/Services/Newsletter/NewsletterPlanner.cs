using System.Security.Cryptography;
using Escalated.Data;
using Escalated.Models;
using Escalated.Models.Newsletter;
using Microsoft.EntityFrameworkCore;
using NewsletterEntity = Escalated.Models.Newsletter.Newsletter;

namespace Escalated.Services.Newsletter;

public class NewsletterPlanner
{
    private const string TokenAlphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    private readonly EscalatedDbContext _db;
    private readonly ContactSegmentResolver _segments;
    private readonly BounceSuppressionStore _bounces;
    private readonly INewsletterClock _clock;

    public NewsletterPlanner(
        EscalatedDbContext db,
        ContactSegmentResolver segments,
        BounceSuppressionStore bounces,
        INewsletterClock clock)
    {
        _db = db;
        _segments = segments;
        _bounces = bounces;
        _clock = clock;
    }

    public async Task PlanAsync(NewsletterEntity newsletter, CancellationToken ct = default)
    {
        var tracked = await _db.Newsletters
            .Include(n => n.TargetList)
            .SingleOrDefaultAsync(n => n.Id == newsletter.Id, ct)
            ?? newsletter;

        tracked.Status = "sending";
        tracked.UpdatedAt = _clock.UtcNow;
        await _db.SaveChangesAsync(ct);

        if (tracked.TargetList is null)
        {
            tracked.TargetList = await _db.NewsletterLists.SingleAsync(l => l.Id == tracked.TargetListId, ct);
        }

        var contactIds = await _segments.ResolveSendableAsync(tracked.TargetList, ct);
        if (contactIds.Count == 0)
        {
            tracked.SummaryTotal = 0;
            tracked.UpdatedAt = _clock.UtcNow;
            await _db.SaveChangesAsync(ct);
            return;
        }

        var contacts = await _db.Contacts
            .Where(c => contactIds.Contains(c.Id))
            .OrderBy(c => c.Id)
            .ToListAsync(ct);

        var sendableEmails = (await _bounces.FilterSendableAsync(contacts.Select(c => c.Email), ct))
            .Select(Contact.NormalizeEmail)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var rows = new List<NewsletterDelivery>();
        foreach (var contact in contacts)
        {
            if (!sendableEmails.Contains(Contact.NormalizeEmail(contact.Email)))
                continue;

            rows.Add(new NewsletterDelivery
            {
                NewsletterId = tracked.Id,
                ContactId = contact.Id,
                EmailAtSend = contact.Email,
                Status = "pending",
                TrackingToken = await GenerateUniqueTokenAsync(ct),
                AttemptCount = 0,
                IsTest = false,
                CreatedAt = _clock.UtcNow,
            });
        }

        if (rows.Count > 0)
            _db.NewsletterDeliveries.AddRange(rows);

        tracked.SummaryTotal = rows.Count;
        tracked.UpdatedAt = _clock.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    private async Task<string> GenerateUniqueTokenAsync(CancellationToken ct)
    {
        string token;
        do
        {
            token = GenerateToken();
        } while (await _db.NewsletterDeliveries.AnyAsync(d => d.TrackingToken == token, ct));

        return token;
    }

    private static string GenerateToken()
    {
        Span<char> chars = stackalloc char[40];
        Span<byte> bytes = stackalloc byte[40];
        RandomNumberGenerator.Fill(bytes);
        for (var i = 0; i < chars.Length; i++)
            chars[i] = TokenAlphabet[bytes[i] % TokenAlphabet.Length];
        return new string(chars);
    }
}
