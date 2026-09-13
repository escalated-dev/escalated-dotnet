using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Escalated.Data;
using Escalated.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Escalated.Services;

public class WebhookDispatcher
{
    private readonly EscalatedDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<WebhookDispatcher> _logger;
    private readonly IHostApplicationLifetime? _lifetime;
    private const int MaxAttempts = 3;

    /// <param name="lifetime">
    /// When the host provides one, retries still waiting out their backoff are dropped
    /// as it stops, instead of waking against a disposed service provider.
    /// </param>
    public WebhookDispatcher(EscalatedDbContext db, IHttpClientFactory httpClientFactory,
        IServiceScopeFactory scopeFactory, TimeProvider timeProvider, ILogger<WebhookDispatcher> logger,
        IHostApplicationLifetime? lifetime = null)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
        _lifetime = lifetime;
    }

    /// <summary>
    /// Dispatch a webhook event to all active subscribers.
    /// </summary>
    public async Task DispatchAsync(string eventName, object payload, CancellationToken ct = default)
    {
        var webhooks = await _db.Webhooks
            .Where(w => w.Active)
            .ToListAsync(ct);

        foreach (var webhook in webhooks)
        {
            if (webhook.SubscribedTo(eventName))
            {
                await SendAsync(webhook, eventName, payload, 1, ct);
            }
        }
    }

    /// <summary>
    /// Send a single webhook delivery with HMAC-SHA256 signing and retry logic.
    /// </summary>
    public async Task SendAsync(Webhook webhook, string eventName, object payload, int attempt = 1,
        CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(new
        {
            @event = eventName,
            payload,
            timestamp = DateTime.UtcNow.ToString("O")
        });

        var delivery = new WebhookDelivery
        {
            WebhookId = webhook.Id,
            Event = eventName,
            Payload = JsonSerializer.Serialize(payload),
            Attempts = attempt,
            CreatedAt = DateTime.UtcNow
        };

        _db.WebhookDeliveries.Add(delivery);
        await _db.SaveChangesAsync(ct);

        if (!TryValidateWebhookUri(webhook.Url, out var webhookUri))
        {
            delivery.ResponseCode = 0;
            delivery.ResponseBody = "Webhook URL must be an absolute HTTP(S) URL that does not target a local or private address.";
            delivery.Attempts = attempt;

            _db.WebhookDeliveries.Update(delivery);
            await _db.SaveChangesAsync(ct);

            _logger.LogWarning("Webhook delivery blocked for webhook {WebhookId}, event {Event}, attempt {Attempt}: unsafe URL",
                webhook.Id, eventName, attempt);
            return;
        }

        try
        {
            var client = _httpClientFactory.CreateClient("EscalatedWebhook");
            client.Timeout = TimeSpan.FromSeconds(10);

            var request = new HttpRequestMessage(HttpMethod.Post, webhookUri)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

            request.Headers.Add("X-Escalated-Event", eventName);

            // HMAC-SHA256 signing
            if (!string.IsNullOrEmpty(webhook.Secret))
            {
                var signature = ComputeHmacSha256(body, webhook.Secret);
                request.Headers.Add("X-Escalated-Signature", signature);
            }

            var response = await client.SendAsync(request, ct);

            delivery.ResponseCode = (int)response.StatusCode;
            var responseBody = await response.Content.ReadAsStringAsync(ct);
            delivery.ResponseBody = responseBody.Length > 2000 ? responseBody[..2000] : responseBody;
            delivery.DeliveredAt = DateTime.UtcNow;
            delivery.Attempts = attempt;

            _db.WebhookDeliveries.Update(delivery);
            await _db.SaveChangesAsync(ct);

            // Retry on failure
            if (!response.IsSuccessStatusCode && attempt < MaxAttempts)
            {
                ScheduleRetry(webhook.Id, eventName, payload, attempt + 1);
            }
        }
        catch (Exception ex)
        {
            delivery.ResponseCode = 0;
            delivery.ResponseBody = ex.Message;
            delivery.Attempts = attempt;

            _db.WebhookDeliveries.Update(delivery);
            await _db.SaveChangesAsync(ct);

            _logger.LogWarning("Webhook delivery failed for webhook {WebhookId}, event {Event}, attempt {Attempt}: {Error}",
                webhook.Id, eventName, attempt, ex.Message);

            if (attempt < MaxAttempts)
            {
                ScheduleRetry(webhook.Id, eventName, payload, attempt + 1);
            }
        }
    }

    /// <summary>
    /// Retry a specific delivery.
    /// </summary>
    public async Task RetryDeliveryAsync(WebhookDelivery delivery, CancellationToken ct = default)
    {
        var webhook = await _db.Webhooks.FindAsync(new object[] { delivery.WebhookId }, ct);
        if (webhook != null)
        {
            var payload = delivery.Payload != null
                ? JsonSerializer.Deserialize<object>(delivery.Payload)
                : new { };
            await SendAsync(webhook, delivery.Event, payload!, 1, ct);
        }
    }

    /// <summary>
    /// Sends <paramref name="attempt"/> once its backoff has passed, without holding up the caller.
    ///
    /// <para>The caller is usually a request, and this dispatcher's context belongs to the
    /// request's scope, or to the scope <c>WebhookEventDispatcher</c> opens for each event.
    /// Either is disposed long before a two-minute backoff ends, so the retry opens a scope
    /// of its own. It reads the webhook again, too: it may have been deactivated or
    /// deleted in the meantime.</para>
    /// </summary>
    private void ScheduleRetry(int webhookId, string eventName, object payload, int attempt)
    {
        var stopping = _lifetime?.ApplicationStopping ?? CancellationToken.None;
        _ = RetryLaterAsync(webhookId, eventName, payload, attempt, stopping);
    }

    private async Task RetryLaterAsync(int webhookId, string eventName, object payload, int attempt,
        CancellationToken ct)
    {
        try
        {
            await Task.Delay(RetryDelay(attempt), _timeProvider, ct);

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EscalatedDbContext>();
            var webhook = await db.Webhooks.FirstOrDefaultAsync(w => w.Id == webhookId, ct);

            if (webhook is not { Active: true })
            {
                _logger.LogInformation("Webhook {WebhookId} is no longer active; dropping attempt {Attempt} of {Event}",
                    webhookId, attempt, eventName);
                return;
            }

            await scope.ServiceProvider.GetRequiredService<WebhookDispatcher>()
                .SendAsync(webhook, eventName, payload, attempt, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The host is stopping. A retry still waiting out its backoff is not sent.
        }
        catch (Exception ex)
        {
            // Nothing awaits this task, so an exception left here would vanish.
            _logger.LogError(ex, "Webhook retry failed for webhook {WebhookId}, event {Event}, attempt {Attempt}",
                webhookId, eventName, attempt);
        }
    }

    /// <summary>2 minutes before the second attempt, 4 before the third.</summary>
    private static TimeSpan RetryDelay(int attempt) => TimeSpan.FromSeconds(Math.Pow(2, attempt) * 30);

    private static string ComputeHmacSha256(string data, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static bool IsSafeWebhookUrl(string webhookUrl)
    {
        return TryValidateWebhookUri(webhookUrl, out _);
    }

    private static bool TryValidateWebhookUri(string webhookUrl, out Uri uri)
    {
        uri = null!;

        if (!Uri.TryCreate(webhookUrl, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        if (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp)
        {
            return false;
        }

        if (IsLocalAddress(parsed.Host))
        {
            return false;
        }

        uri = parsed;
        return true;
    }

    private static bool IsLocalAddress(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("localhost.localdomain", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(host, out var address) && IsPrivateOrLocalAddress(address);
    }

    private static bool IsPrivateOrLocalAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) ||
            address.Equals(IPAddress.Any) ||
            address.Equals(IPAddress.IPv6Any))
        {
            return true;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return bytes[0] == 0 ||
                   bytes[0] == 10 ||
                   bytes[0] == 127 ||
                   bytes[0] == 169 && bytes[1] == 254 ||
                   bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31 ||
                   bytes[0] == 192 && bytes[1] == 168 ||
                   bytes[0] >= 224;
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal ||
                   address.IsIPv6Multicast ||
                   address.IsIPv6SiteLocal ||
                   bytes[0] == 0xfc ||
                   bytes[0] == 0xfd;
        }

        return false;
    }
}
