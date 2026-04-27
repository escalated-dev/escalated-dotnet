using System.Text.Json;

namespace Escalated.Services.Email.Inbound;

/// <summary>
/// Parses Mailgun's inbound webhook payload into an
/// <see cref="InboundMessage"/>. Mailgun posts
/// <c>multipart/form-data</c> with field names like
/// <c>sender</c>, <c>recipient</c>, <c>subject</c>, <c>body-plain</c>,
/// <c>body-html</c>, <c>Message-Id</c>, <c>In-Reply-To</c>,
/// <c>References</c>, and JSON-encoded <c>attachments</c>. We accept
/// this shape as a <see cref="IDictionary{TKey, TValue}"/> or
/// <see cref="JsonElement"/> — the controller pre-decodes either.
/// </summary>
public class MailgunInboundParser : IInboundEmailParser
{
    public string Name => "mailgun";

    public Task<InboundMessage> ParseAsync(object rawPayload, CancellationToken ct = default)
    {
        var get = BuildLookup(rawPayload);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Message-ID"] = get("Message-Id") ?? string.Empty,
            ["In-Reply-To"] = get("In-Reply-To") ?? string.Empty,
            ["References"] = get("References") ?? string.Empty,
        };
        // Strip empty values so downstream checks don't see blank strings.
        foreach (var key in headers.Keys.ToList())
        {
            if (string.IsNullOrEmpty(headers[key])) headers.Remove(key);
        }

        return Task.FromResult(new InboundMessage
        {
            FromEmail = get("sender") ?? get("from") ?? string.Empty,
            FromName = ExtractFromName(get("from")),
            ToEmail = get("recipient") ?? get("To") ?? string.Empty,
            Subject = get("subject") ?? string.Empty,
            BodyText = get("body-plain"),
            BodyHtml = get("body-html"),
            MessageId = get("Message-Id"),
            InReplyTo = get("In-Reply-To"),
            References = get("References"),
            Headers = headers,
            Attachments = ExtractAttachments(get("attachments")),
        });
    }

    private static Func<string, string?> BuildLookup(object payload)
    {
        return payload switch
        {
            IDictionary<string, string> map => key => map.TryGetValue(key, out var v) ? v : null,
            IDictionary<string, object> objMap => key =>
                objMap.TryGetValue(key, out var v) ? v?.ToString() : null,
            JsonElement el => key =>
            {
                foreach (var prop in el.EnumerateObject())
                {
                    if (string.Equals(prop.Name, key, StringComparison.Ordinal))
                    {
                        return prop.Value.ValueKind == JsonValueKind.String
                            ? prop.Value.GetString()
                            : prop.Value.ToString();
                    }
                }
                return null;
            },
            _ => (_ => null),
        };
    }

    /// <summary>
    /// Mailgun's <c>from</c> field is typically
    /// <c>"Full Name &lt;email@host&gt;"</c>. Extract the display name
    /// portion; returns <c>null</c> if no angle-bracketed email.
    /// </summary>
    private static string? ExtractFromName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || !raw.Contains('<')) return null;
        var name = raw.Substring(0, raw.IndexOf('<')).Trim().Trim('"');
        return string.IsNullOrEmpty(name) ? null : name;
    }

    /// <summary>
    /// Mailgun serializes attachments as a JSON-encoded string (list
    /// of objects with <c>name</c> / <c>content-type</c> / <c>size</c>
    /// / <c>url</c>). Large attachments are provider-hosted behind
    /// <c>url</c>; we pass the URL through so a follow-up worker can
    /// download them out-of-band.
    /// </summary>
    private static List<InboundAttachment> ExtractAttachments(string? attachmentsJson)
    {
        if (string.IsNullOrEmpty(attachmentsJson)) return new();

        try
        {
            var entries = JsonSerializer.Deserialize<JsonElement[]>(attachmentsJson) ?? [];
            var list = new List<InboundAttachment>();
            foreach (var entry in entries)
            {
                var name = entry.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                    ? n.GetString() ?? "attachment"
                    : "attachment";
                var contentType = entry.TryGetProperty("content-type", out var ct) && ct.ValueKind == JsonValueKind.String
                    ? ct.GetString() ?? "application/octet-stream"
                    : "application/octet-stream";
                var size = entry.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number
                    ? s.GetInt64()
                    : (long?)null;
                var url = entry.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String
                    ? u.GetString()
                    : null;

                list.Add(new InboundAttachment
                {
                    Name = name,
                    ContentType = contentType,
                    SizeBytes = size,
                    DownloadUrl = url,
                });
            }
            return list;
        }
        catch (JsonException)
        {
            return new();
        }
    }
}
