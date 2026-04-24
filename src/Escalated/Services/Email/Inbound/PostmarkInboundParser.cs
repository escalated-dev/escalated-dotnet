using System.Text.Json;

namespace Escalated.Services.Email.Inbound;

/// <summary>
/// Parses Postmark's inbound webhook payload into an
/// <see cref="InboundMessage"/>. Postmark posts a JSON body with
/// <c>FromFull</c> / <c>ToFull</c> / <c>Subject</c> / <c>TextBody</c> /
/// <c>HtmlBody</c> / <c>Headers</c> / <c>Attachments</c> fields.
/// </summary>
public class PostmarkInboundParser : IInboundEmailParser
{
    public string Name => "postmark";

    public Task<InboundMessage> ParseAsync(object rawPayload, CancellationToken ct = default)
    {
        // Accept either a JsonElement or something that serializes to one.
        var root = rawPayload switch
        {
            JsonElement el => el,
            _ => JsonSerializer.SerializeToElement(rawPayload),
        };

        var from = root.TryGetProperty("FromFull", out var fromFull) && fromFull.ValueKind == JsonValueKind.Object
            ? new
            {
                Email = TryGetString(fromFull, "Email") ?? string.Empty,
                Name = TryGetString(fromFull, "Name"),
            }
            : new { Email = TryGetString(root, "From") ?? string.Empty, Name = (string?)null };

        var toEmail = TryGetString(root, "OriginalRecipient")
                      ?? FirstToEmail(root)
                      ?? TryGetString(root, "To")
                      ?? string.Empty;

        var headers = ExtractHeaders(root);

        var message = new InboundMessage
        {
            FromEmail = from.Email,
            FromName = from.Name,
            ToEmail = toEmail,
            Subject = TryGetString(root, "Subject") ?? string.Empty,
            BodyText = TryGetString(root, "TextBody"),
            BodyHtml = TryGetString(root, "HtmlBody"),
            MessageId = TryGetString(root, "MessageID") ?? headers.GetValueOrDefault("Message-ID"),
            InReplyTo = headers.GetValueOrDefault("In-Reply-To"),
            References = headers.GetValueOrDefault("References"),
            Headers = headers,
            Attachments = ExtractAttachments(root),
        };

        return Task.FromResult(message);
    }

    private static string? TryGetString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var prop)) return null;
        return prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;
    }

    private static string? FirstToEmail(JsonElement root)
    {
        if (!root.TryGetProperty("ToFull", out var toFull) || toFull.ValueKind != JsonValueKind.Array)
        {
            return null;
        }
        foreach (var entry in toFull.EnumerateArray())
        {
            var email = TryGetString(entry, "Email");
            if (!string.IsNullOrEmpty(email)) return email;
        }
        return null;
    }

    private static Dictionary<string, string> ExtractHeaders(JsonElement root)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty("Headers", out var list) || list.ValueKind != JsonValueKind.Array)
        {
            return headers;
        }
        foreach (var entry in list.EnumerateArray())
        {
            var name = TryGetString(entry, "Name");
            var value = TryGetString(entry, "Value");
            if (!string.IsNullOrEmpty(name) && value is not null)
            {
                headers[name] = value;
            }
        }
        return headers;
    }

    private static List<InboundAttachment> ExtractAttachments(JsonElement root)
    {
        var list = new List<InboundAttachment>();
        if (!root.TryGetProperty("Attachments", out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return list;
        }
        foreach (var entry in arr.EnumerateArray())
        {
            var name = TryGetString(entry, "Name") ?? "attachment";
            var contentType = TryGetString(entry, "ContentType") ?? "application/octet-stream";
            var size = entry.TryGetProperty("ContentLength", out var s) && s.ValueKind == JsonValueKind.Number
                ? s.GetInt64()
                : (long?)null;
            var contentBase64 = TryGetString(entry, "Content");
            var content = !string.IsNullOrEmpty(contentBase64)
                ? Convert.FromBase64String(contentBase64)
                : null;

            list.Add(new InboundAttachment
            {
                Name = name,
                ContentType = contentType,
                SizeBytes = size,
                Content = content,
            });
        }
        return list;
    }
}
