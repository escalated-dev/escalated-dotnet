using Escalated.Models.Newsletter;

namespace Escalated.Services.Newsletter;

public interface INewsletterClock
{
    DateTime UtcNow { get; }
}

public class SystemNewsletterClock : INewsletterClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}

public interface INewsletterEmailSender
{
    Task SendAsync(NewsletterDelivery delivery, string html, CancellationToken ct = default);
}

public class NullNewsletterEmailSender : INewsletterEmailSender
{
    public Task SendAsync(NewsletterDelivery delivery, string html, CancellationToken ct = default) =>
        throw new InvalidOperationException("Newsletter mailer is not configured.");
}

public interface INewsletterRateLimitStore
{
    int Get(string key);
    void Put(string key, int value, DateTime expiresAtUtc);
}

public class MemoryNewsletterRateLimitStore : INewsletterRateLimitStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, (int Count, DateTime ExpiresAtUtc)> _entries = new();

    public int Get(string key)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var entry))
                return 0;

            if (entry.ExpiresAtUtc <= DateTime.UtcNow)
            {
                _entries.Remove(key);
                return 0;
            }

            return entry.Count;
        }
    }

    public void Put(string key, int value, DateTime expiresAtUtc)
    {
        lock (_gate)
        {
            _entries[key] = (value, expiresAtUtc);
        }
    }
}
