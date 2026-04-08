using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;

namespace Escalated.Middleware;

/// <summary>
/// Simple sliding-window rate limiter for the Escalated API.
/// </summary>
public class EscalatedRateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly int _maxRequests;
    private readonly TimeSpan _window;
    private static readonly ConcurrentDictionary<string, List<DateTime>> _requestLog = new();

    public EscalatedRateLimitMiddleware(RequestDelegate next, int maxRequests = 60, int windowSeconds = 60)
    {
        _next = next;
        _maxRequests = maxRequests;
        _window = TimeSpan.FromSeconds(windowSeconds);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var key = GetClientKey(context);
        var now = DateTime.UtcNow;

        var requests = _requestLog.GetOrAdd(key, _ => new List<DateTime>());

        lock (requests)
        {
            requests.RemoveAll(t => now - t > _window);

            if (requests.Count >= _maxRequests)
            {
                context.Response.StatusCode = 429;
                context.Response.Headers["Retry-After"] = _window.TotalSeconds.ToString("F0");
                context.Response.WriteAsJsonAsync(new { error = "Rate limit exceeded." }).Wait();
                return;
            }

            requests.Add(now);
        }

        context.Response.Headers["X-RateLimit-Limit"] = _maxRequests.ToString();
        context.Response.Headers["X-RateLimit-Remaining"] =
            Math.Max(0, _maxRequests - requests.Count).ToString();

        await _next(context);
    }

    private static string GetClientKey(HttpContext context)
    {
        // Prefer API token ID, fall back to IP
        if (context.Items.TryGetValue("EscalatedApiTokenId", out var tokenId) && tokenId != null)
            return $"token:{tokenId}";

        return $"ip:{context.Connection.RemoteIpAddress}";
    }
}
