using System.Threading.RateLimiting;
using Escalated.Configuration;
using Escalated.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace Escalated.Controllers.Widget;

/// <summary>
/// Rate-limits a guest endpoint per client IP against the
/// <paramref name="scope"/> counter (see <see cref="GuestRateLimitOptions"/>).
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class GuestThrottleAttribute : TypeFilterAttribute
{
    public GuestThrottleAttribute(GuestThrottleScope scope) : base(typeof(GuestThrottleFilter))
    {
        Arguments = new object[] { scope };
    }
}

/// <summary>
/// A resource filter, so it runs before model binding and before the action
/// looks up the guest token: malformed bodies and wrong-token guesses are
/// counted too, and a token cannot be guessed at speed.
///
/// <para>The client IP is <c>Connection.RemoteIpAddress</c>. Behind a proxy the
/// host must configure trusted proxies and <c>UseForwardedHeaders()</c>, or every
/// guest shares the proxy's address.</para>
/// </summary>
public sealed class GuestThrottleFilter : IAsyncResourceFilter
{
    private readonly GuestThrottleScope _scope;
    private readonly IGuestRateLimiter _limiter;
    private readonly IOptions<EscalatedOptions> _options;

    public GuestThrottleFilter(GuestThrottleScope scope, IGuestRateLimiter limiter, IOptions<EscalatedOptions> options)
    {
        _scope = scope;
        _limiter = limiter;
        _options = options;
    }

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        if (!_options.Value.GuestRateLimit.Enabled)
        {
            await next();
            return;
        }

        var http = context.HttpContext;
        var address = http.Connection.RemoteIpAddress;
        if (address is { IsIPv4MappedToIPv6: true })
        {
            address = address.MapToIPv4();
        }

        using (var lease = await _limiter.AcquireAsync(_scope, address?.ToString() ?? "unknown", http.RequestAborted))
        {
            if (!lease.IsAcquired)
            {
                var retryAfter = lease.TryGetMetadata(MetadataName.RetryAfter, out var wait)
                    ? Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds))
                    : (int)MemoryGuestRateLimiter.Window.TotalSeconds;

                http.Response.Headers.RetryAfter = retryAfter.ToString(System.Globalization.CultureInfo.InvariantCulture);
                context.Result = new ObjectResult(new { error = "Too many requests. Please try again later." })
                {
                    StatusCode = StatusCodes.Status429TooManyRequests,
                };
                return;
            }
        }

        await next();
    }
}
