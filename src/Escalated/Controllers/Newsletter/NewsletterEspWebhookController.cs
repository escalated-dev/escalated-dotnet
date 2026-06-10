using System.Text.Json;
using System.Text.RegularExpressions;
using Escalated.Data;
using Escalated.Models;
using Escalated.Services.Newsletter;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Controllers.Newsletter;

[ApiController]
[NewsletterEnabled]
public class NewsletterEspWebhookController : ControllerBase
{
    private static readonly Regex MessageIdTokenRegex = new(
        @"n-\d+-([A-Za-z0-9]+)@",
        RegexOptions.Compiled);

    private static readonly Regex LocalMessageIdTokenRegex = new(
        @"^n-\d+-([A-Za-z0-9]+)$",
        RegexOptions.Compiled);

    private static readonly HashSet<string> PostmarkHardBounceTypes = new(StringComparer.Ordinal)
    {
        "HardBounce",
        "BadEmailAddress",
        "BlockedRecipient",
    };

    private readonly NewsletterTracker _tracker;
    private readonly EscalatedDbContext _db;

    public NewsletterEspWebhookController(NewsletterTracker tracker, EscalatedDbContext db)
    {
        _tracker = tracker;
        _db = db;
    }

    [HttpPost]
    public async Task<IActionResult> Postmark([FromBody] JsonElement body, CancellationToken ct)
    {
        var token = await ResolveTokenAsync(
            TokenFromMessageId(GetString(body, "MessageID") ?? string.Empty),
            GetString(body, "Email"),
            ct);

        switch (GetString(body, "RecordType"))
        {
            case "Open":
                await _tracker.RecordOpenAsync(token, ct);
                break;
            case "Click":
                await _tracker.RecordClickAsync(token, GetString(body, "OriginalLink") ?? string.Empty, ct);
                break;
            case "Bounce":
                await _tracker.RecordBounceAsync(
                    token,
                    PostmarkHardBounceTypes.Contains(GetString(body, "Type") ?? string.Empty) ? "hard" : "soft",
                    GetString(body, "Description"),
                    ct);
                break;
            case "SpamComplaint":
                await _tracker.RecordComplaintAsync(token, ct);
                break;
        }

        return Ok(new { ok = true });
    }

    [HttpPost]
    public async Task<IActionResult> Mailgun([FromBody] JsonElement body, CancellationToken ct)
    {
        if (!body.TryGetProperty("event-data", out var eventData))
            return Ok(new { ok = true });

        var messageId = string.Empty;
        if (eventData.TryGetProperty("message", out var message) &&
            message.TryGetProperty("headers", out var headers) &&
            headers.TryGetProperty("message-id", out var messageIdEl))
        {
            messageId = messageIdEl.GetString() ?? string.Empty;
        }

        var recipient = string.Empty;
        if (eventData.TryGetProperty("recipient", out var recipientEl))
            recipient = recipientEl.GetString() ?? string.Empty;

        var token = await ResolveTokenAsync(TokenFromMessageId(messageId), recipient, ct);

        switch (GetString(eventData, "event"))
        {
            case "opened":
                await _tracker.RecordOpenAsync(token, ct);
                break;
            case "clicked":
                await _tracker.RecordClickAsync(token, GetString(eventData, "url") ?? string.Empty, ct);
                break;
            case "failed":
                var severity = GetString(eventData, "severity");
                var description = string.Empty;
                if (eventData.TryGetProperty("delivery-status", out var deliveryStatus))
                    description = GetString(deliveryStatus, "description") ?? string.Empty;
                await _tracker.RecordBounceAsync(
                    token,
                    severity == "permanent" ? "hard" : "soft",
                    description,
                    ct);
                break;
            case "complained":
                await _tracker.RecordComplaintAsync(token, ct);
                break;
        }

        return Ok(new { ok = true });
    }

    [HttpPost]
    public async Task<IActionResult> Ses([FromBody] JsonElement body, CancellationToken ct)
    {
        JsonElement message;
        if (body.TryGetProperty("Message", out var messageEl))
        {
            message = messageEl.ValueKind == JsonValueKind.String
                ? JsonDocument.Parse(messageEl.GetString() ?? "{}").RootElement
                : messageEl;
        }
        else
        {
            message = body;
        }

        var messageId = string.Empty;
        var recipient = string.Empty;
        if (message.TryGetProperty("mail", out var mail))
        {
            if (mail.TryGetProperty("messageId", out var messageIdEl))
                messageId = messageIdEl.GetString() ?? string.Empty;

            if (mail.TryGetProperty("destination", out var destinations) &&
                destinations.ValueKind == JsonValueKind.Array &&
                destinations.GetArrayLength() > 0)
            {
                recipient = destinations[0].GetString() ?? string.Empty;
            }
        }

        var token = await ResolveTokenAsync(TokenFromMessageId(messageId), recipient, ct);

        var eventType = GetString(message, "eventType") ?? GetString(message, "notificationType");
        switch (eventType)
        {
            case "Open":
                await _tracker.RecordOpenAsync(token, ct);
                break;
            case "Click":
                var link = string.Empty;
                if (message.TryGetProperty("click", out var click))
                    link = GetString(click, "link") ?? string.Empty;
                await _tracker.RecordClickAsync(token, link, ct);
                break;
            case "Bounce":
                var bounceType = string.Empty;
                var bounceSubType = string.Empty;
                if (message.TryGetProperty("bounce", out var bounce))
                {
                    bounceType = GetString(bounce, "bounceType") ?? string.Empty;
                    bounceSubType = GetString(bounce, "bounceSubType") ?? string.Empty;
                }

                await _tracker.RecordBounceAsync(
                    token,
                    bounceType == "Permanent" ? "hard" : "soft",
                    bounceSubType,
                    ct);
                break;
            case "Complaint":
                await _tracker.RecordComplaintAsync(token, ct);
                break;
        }

        return Ok(new { ok = true });
    }

    [HttpPost]
    public async Task<IActionResult> Sendgrid([FromBody] JsonElement body, CancellationToken ct)
    {
        if (body.ValueKind != JsonValueKind.Array)
            return Ok(new { ok = true });

        foreach (var evt in body.EnumerateArray())
        {
            var messageId = GetString(evt, "smtp-id") ?? GetString(evt, "sg_message_id") ?? string.Empty;
            var recipient = GetString(evt, "email") ?? string.Empty;
            var token = await ResolveTokenAsync(TokenFromMessageId(messageId), recipient, ct);

            switch (GetString(evt, "event"))
            {
                case "open":
                    await _tracker.RecordOpenAsync(token, ct);
                    break;
                case "click":
                    await _tracker.RecordClickAsync(token, GetString(evt, "url") ?? string.Empty, ct);
                    break;
                case "bounce":
                    await _tracker.RecordBounceAsync(token, "hard", GetString(evt, "reason"), ct);
                    break;
                case "dropped":
                    await _tracker.RecordBounceAsync(token, "hard", GetString(evt, "reason"), ct);
                    break;
                case "spamreport":
                    await _tracker.RecordComplaintAsync(token, ct);
                    break;
            }
        }

        return Ok(new { ok = true });
    }

    private async Task<string> ResolveTokenAsync(string token, string? recipientEmail, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(token))
            return token;

        if (string.IsNullOrWhiteSpace(recipientEmail))
            return string.Empty;

        // TODO: Outbound sender does not yet tag all ESP payloads with X-Escalated tracking metadata;
        // fall back to the recipient's most recent delivery when Message-ID parsing fails.
        var normalized = Contact.NormalizeEmail(recipientEmail);
        var delivery = await _db.NewsletterDeliveries
            .Where(d => d.EmailAtSend == normalized)
            .OrderByDescending(d => d.Id)
            .Select(d => d.TrackingToken)
            .FirstOrDefaultAsync(ct);

        return delivery ?? string.Empty;
    }

    private static string TokenFromMessageId(string messageId)
    {
        var match = MessageIdTokenRegex.Match(messageId);
        if (match.Success)
            return match.Groups[1].Value;

        var local = messageId.Split('@')[0];
        var localMatch = LocalMessageIdTokenRegex.Match(local);
        return localMatch.Success ? localMatch.Groups[1].Value : string.Empty;
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
