using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.RateLimiting;
using Escalated.Configuration;
using Escalated.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Escalated.Tests.Hosting;

/// <summary>
/// The guest widget endpoints are unauthenticated and every accepted request
/// writes rows and sends mail, so the package itself caps them per client IP
/// (ticket creation 5/min, guest replies 10/min by default) instead of relying
/// on each host to put a limiter in front. Mirrors escalated-nestjs#130.
/// </summary>
public class GuestThrottleTests
{
    private const string ClientIpHeader = "X-Test-Client-Ip";

    [Fact]
    public async Task SixthGuestTicketFromOneIpWithinAMinute_Gets429()
    {
        await using var host = await StartAsync();

        var statuses = await CreateTicketsAsync(host, 6);

        Assert.Equal(new[] { 200, 200, 200, 200, 200, 429 }, statuses);
        Assert.Equal(5, await host.QueryAsync(db => Task.FromResult(db.Tickets.Count())));
    }

    [Fact]
    public async Task RejectedRequest_CarriesRetryAfter()
    {
        await using var host = await StartAsync(o => o.GuestRateLimit.TicketsPerMinute = 1);
        await CreateTicketsAsync(host, 1);

        var response = await PostTicketAsync(host, 99);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Retry-After", out var values));
        var seconds = int.Parse(values!.Single());
        Assert.InRange(seconds, 1, 60);
    }

    [Fact]
    public async Task EleventhGuestReplyFromOneIpWithinAMinute_Gets429()
    {
        await using var host = await StartAsync();
        var token = await GuestTokenAsync(host);

        var statuses = await RepliesAsync(host, token, 11);

        Assert.Equal(Enumerable.Repeat(200, 10), statuses.Take(10));
        Assert.Equal(429, statuses[10]);
    }

    [Fact]
    public async Task RepliesWithAWrongGuestToken_AreCounted()
    {
        await using var host = await StartAsync(o => o.GuestRateLimit.RepliesPerMinute = 2);

        var statuses = await RepliesAsync(host, "not-a-real-token", 3);

        Assert.Equal(new[] { 404, 404, 429 }, statuses);
    }

    [Fact]
    public async Task TicketsAndRepliesAreCountedSeparately()
    {
        await using var host = await StartAsync();
        var token = await GuestTokenAsync(host);
        await CreateTicketsAsync(host, 4);

        Assert.Equal(new[] { 200 }, await RepliesAsync(host, token, 1));
    }

    [Fact]
    public async Task EachClientIpIsCountedSeparately()
    {
        await using var host = await StartAsync(o => o.GuestRateLimit.TicketsPerMinute = 1);
        await CreateTicketsAsync(host, 1, "203.0.113.1");

        Assert.Equal(new[] { 200 }, await CreateTicketsAsync(host, 1, "203.0.113.2"));
    }

    [Fact]
    public async Task ConfiguredLimitIsHonoured()
    {
        await using var host = await StartAsync(o => o.GuestRateLimit.TicketsPerMinute = 2);

        Assert.Equal(new[] { 200, 200, 429 }, await CreateTicketsAsync(host, 3));
    }

    [Fact]
    public async Task Disabled_NeverAnswers429()
    {
        await using var host = await StartAsync(o => o.GuestRateLimit.Enabled = false);

        var statuses = await CreateTicketsAsync(host, 8);

        Assert.All(statuses, s => Assert.Equal(200, s));
    }

    [Fact]
    public async Task CountsInAHostRegisteredLimiter()
    {
        var shared = new RecordingLimiter();
        await using var host = await EscalatedTestHost.StartAsync(null, services =>
        {
            services.AddSingleton<IStartupFilter, ClientIpFromHeader>();
            services.AddSingleton<IGuestRateLimiter>(shared);
        });

        var request = new HttpRequestMessage(HttpMethod.Post, "/support/widget/tickets/any-token/reply")
        {
            Content = JsonContent.Create(new { body = "hi" }),
        };
        request.Headers.Add(ClientIpHeader, "203.0.113.9");
        var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(new[] { (GuestThrottleScope.Reply, "203.0.113.9") }, shared.Calls);
    }

    [Fact]
    public void Defaults_MatchTheReference()
    {
        var options = new EscalatedOptions();

        Assert.True(options.GuestRateLimit.Enabled);
        Assert.Equal(5, options.GuestRateLimit.TicketsPerMinute);
        Assert.Equal(10, options.GuestRateLimit.RepliesPerMinute);
    }

    private static Task<EscalatedTestHost> StartAsync(Action<EscalatedOptions>? configure = null) =>
        EscalatedTestHost.StartAsync(configure, services => services.AddSingleton<IStartupFilter, ClientIpFromHeader>());

    private static Task<HttpResponseMessage> PostTicketAsync(EscalatedTestHost host, int n, string? ip = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/support/widget/tickets")
        {
            // A distinct email per call, so only the per-IP limit can be what trips.
            Content = JsonContent.Create(new { subject = "Help", description = "d", name = "Guest", email = $"guest{n}@example.com" }),
        };
        if (ip is not null) request.Headers.Add(ClientIpHeader, ip);
        return host.Client.SendAsync(request);
    }

    private static async Task<int[]> CreateTicketsAsync(EscalatedTestHost host, int times, string? ip = null)
    {
        var statuses = new int[times];
        for (var i = 0; i < times; i++)
        {
            statuses[i] = (int)(await PostTicketAsync(host, i, ip)).StatusCode;
        }

        return statuses;
    }

    private static async Task<int[]> RepliesAsync(EscalatedTestHost host, string token, int times)
    {
        var statuses = new int[times];
        for (var i = 0; i < times; i++)
        {
            var response = await host.Client.PostAsJsonAsync($"/support/widget/tickets/{token}/reply", new { body = "hi" });
            statuses[i] = (int)response.StatusCode;
        }

        return statuses;
    }

    private static async Task<string> GuestTokenAsync(EscalatedTestHost host)
    {
        var response = await PostTicketAsync(host, 1000);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("guestToken").GetString()!;
    }

    /// <summary>
    /// TestServer leaves <c>RemoteIpAddress</c> unset. Stand in for the transport:
    /// every request comes from 203.0.113.1 unless a test names another address.
    /// </summary>
    private sealed class ClientIpFromHeader : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                var header = context.Request.Headers[ClientIpHeader].ToString();
                context.Connection.RemoteIpAddress = IPAddress.Parse(header.Length > 0 ? header : "203.0.113.1");
                return nextMiddleware(context);
            });
            next(app);
        };
    }

    /// <summary>A stand-in for a shared (e.g. Redis-backed) limiter that refuses everything.</summary>
    private sealed class RecordingLimiter : IGuestRateLimiter
    {
        public List<(GuestThrottleScope, string)> Calls { get; } = new();

        public ValueTask<RateLimitLease> AcquireAsync(GuestThrottleScope scope, string clientIp, CancellationToken cancellationToken = default)
        {
            Calls.Add((scope, clientIp));
            return ValueTask.FromResult<RateLimitLease>(new RefusedLease());
        }
    }

    private sealed class RefusedLease : RateLimitLease
    {
        public override bool IsAcquired => false;

        public override IEnumerable<string> MetadataNames => new[] { MetadataName.RetryAfter.Name };

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            metadata = TimeSpan.FromSeconds(30);
            return metadataName == MetadataName.RetryAfter.Name;
        }
    }
}
