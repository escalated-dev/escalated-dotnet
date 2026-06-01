using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Escalated.Data;
using Escalated.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Escalated.Services;

public class WebhookDispatcher
{
    private readonly EscalatedDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WebhookDispatcher> _logger;
    private const int MaxAttempts = 3;

    public WebhookDispatcher(EscalatedDbContext db, IHttpClientFactory httpClientFactory,
        ILogger<WebhookDispatcher> logger)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
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
                _ = RetryLaterAsync(webhook, eventName, payload, attempt + 1);
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
                _ = RetryLaterAsync(webhook, eventName, payload, attempt + 1);
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

    private async Task RetryLaterAsync(Webhook webhook, string eventName, object payload, int attempt)
    {
        var delaySeconds = (int)Math.Pow(2, attempt) * 30;
        await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
        await SendAsync(webhook, eventName, payload, attempt);
    }

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
