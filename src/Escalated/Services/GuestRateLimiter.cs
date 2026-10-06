using System.Threading.RateLimiting;
using Escalated.Configuration;
using Microsoft.Extensions.Options;

namespace Escalated.Services;

/// <summary>The guest endpoint a request is counted against. Each has its own counter.</summary>
public enum GuestThrottleScope
{
    Ticket,
    Reply,
}

/// <summary>
/// Counts guest ticket submissions and replies per client IP. Register your own
/// implementation before <c>AddEscalated()</c> to keep the counters somewhere
/// every instance shares (e.g. Redis); the default keeps them in process memory.
/// </summary>
public interface IGuestRateLimiter
{
    /// <summary>
    /// Takes one permit from the <paramref name="scope"/> counter for
    /// <paramref name="clientIp"/>. A lease that is not acquired should carry
    /// <see cref="MetadataName.RetryAfter"/>.
    /// </summary>
    ValueTask<RateLimitLease> AcquireAsync(GuestThrottleScope scope, string clientIp, CancellationToken cancellationToken = default);
}

/// <summary>
/// The default <see cref="IGuestRateLimiter"/>: a built-in
/// <see cref="PartitionedRateLimiter"/> with one fixed 60-second window per
/// scope and client IP. Per process, so each instance of a multi-instance
/// deployment counts on its own.
/// </summary>
public sealed class MemoryGuestRateLimiter : IGuestRateLimiter, IDisposable
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly PartitionedRateLimiter<(GuestThrottleScope Scope, string Ip)> _limiter;

    public MemoryGuestRateLimiter(IOptions<EscalatedOptions> options)
    {
        var config = options.Value.GuestRateLimit;

        _limiter = PartitionedRateLimiter.Create<(GuestThrottleScope Scope, string Ip), (GuestThrottleScope Scope, string Ip)>(
            key => RateLimitPartition.GetFixedWindowLimiter(key, k => new FixedWindowRateLimiterOptions
            {
                PermitLimit = Math.Max(1, k.Scope == GuestThrottleScope.Ticket ? config.TicketsPerMinute : config.RepliesPerMinute),
                Window = Window,
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
    }

    public ValueTask<RateLimitLease> AcquireAsync(GuestThrottleScope scope, string clientIp, CancellationToken cancellationToken = default) =>
        _limiter.AcquireAsync((scope, clientIp), 1, cancellationToken);

    public void Dispose() => _limiter.Dispose();
}
