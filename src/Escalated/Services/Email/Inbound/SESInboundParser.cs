using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using System.Text.Json;

namespace Escalated.Services.Email.Inbound;

/// <summary>
/// Parses AWS SES inbound mail delivered via SNS HTTP subscription.
/// SES receipt rules publish to an SNS topic; host apps subscribe
/// via HTTP and SNS POSTs the envelope to the unified webhook at
/// <c>/support/webhook/email/inbound?adapter=ses</c>.
///
/// <para>Handles two SNS envelope types:</para>
/// <list type="bullet">
///   <item><description><c>SubscriptionConfirmation</c> — one-time
///     on subscription setup. <see cref="ParseAsync"/> throws
///     <see cref="SESSubscriptionConfirmationException"/> carrying
///     the <c>SubscribeURL</c> that the host must GET out-of-band
///     to activate the subscription.</description></item>
///   <item><description><c>Notification</c> — the actual inbound
///     delivery. The inner <c>Message</c> is a JSON-encoded SES
///     notification with <c>mail.commonHeaders</c> (from/to/subject)
///     and a base64-encoded raw MIME <c>content</c> field (when the
///     receipt rule action is <c>SNS</c> with <c>BASE64</c>
///     encoding).</description></item>
/// </list>
///
/// <para>Body extraction is best-effort: single-part text/plain +
/// text/html, plus multipart/alternative bodies are decoded from
/// the raw MIME content when supplied. Missing content leaves the
/// body empty — the router still resolves via threading metadata
/// pulled from <c>commonHeaders</c> + <c>headers</c>.</para>
/// </summary>
public class SESInboundParser : IInboundEmailParser
{
    public string Name => "ses";

    public Task<InboundMessage> ParseAsync(object rawPayload, CancellationToken ct = default)
    {
        var root = rawPayload switch
        {
            JsonElement el => el,
            _ => JsonSerializer.SerializeToElement(rawPayload),
        };

        var snsType = TryGetString(root, "Type") ?? string.Empty;
        switch (snsType)
        {
            case "SubscriptionConfirmation":
                throw new SESSubscriptionConfirmationException(
                    topicArn: TryGetString(root, "TopicArn") ?? string.Empty,
                    subscribeUrl: TryGetString(root, "SubscribeURL") ?? string.Empty,
                    token: TryGetString(root, "Token") ?? string.Empty);

            case "Notification":
                break;

            default:
                throw new InvalidOperationException(
                    $"Unsupported SNS envelope type: \"{snsType}\"");
        }

        var messageJson = TryGetString(root, "Message")
            ?? throw new InvalidOperationException("SES notification has no Message body");

        JsonElement notification;
        try
        {
            using var doc = JsonDocument.Parse(messageJson);
            notification = doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                "SES notification Message is not valid JSON", ex);
        }

        var mail = notification.TryGetProperty("mail", out var mailEl) && mailEl.ValueKind == JsonValueKind.Object
            ? mailEl
            : default;

        var common = mail.ValueKind == JsonValueKind.Object
            && mail.TryGetProperty("commonHeaders", out var commonEl)
            && commonEl.ValueKind == JsonValueKind.Object
                ? commonEl
                : default;

        var (fromEmail, fromName) = ParseFirstAddressList(common, "from");
        var (toEmail, _) = ParseFirstAddressList(common, "to");

        var subject = common.ValueKind == JsonValueKind.Object
            ? TryGetString(common, "subject") ?? string.Empty
            : string.Empty;
        var messageId = common.ValueKind == JsonValueKind.Object ? TryGetString(common, "messageId") : null;
        var inReplyTo = common.ValueKind == JsonValueKind.Object ? TryGetString(common, "inReplyTo") : null;
        var references = common.ValueKind == JsonValueKind.Object ? TryGetString(common, "references") : null;

        var headers = ExtractHeaders(mail);
        // Fallbacks when commonHeaders is missing the threading fields.
        messageId ??= headers.GetValueOrDefault("Message-ID");
        inReplyTo ??= headers.GetValueOrDefault("In-Reply-To");
        references ??= headers.GetValueOrDefault("References");

        var (bodyText, bodyHtml) = ExtractBody(notification);

        return Task.FromResult(new InboundMessage
        {
            FromEmail = fromEmail,
            FromName = fromName,
            ToEmail = toEmail,
            Subject = subject,
            BodyText = bodyText,
            BodyHtml = bodyHtml,
            MessageId = messageId,
            InReplyTo = inReplyTo,
            References = references,
            Headers = headers,
        });
    }

    private static string? TryGetString(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var prop))
        {
            return null;
        }
        return prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;
    }

    /// <summary>
    /// SES's <c>commonHeaders.from</c> / <c>.to</c> are arrays of
    /// RFC 5322 strings (e.g. <c>["Alice &lt;alice@example.com&gt;"]</c>).
    /// Returns the first entry's email + optional display name.
    /// </summary>
    private static (string Email, string? Name) ParseFirstAddressList(JsonElement common, string name)
    {
        if (common.ValueKind != JsonValueKind.Object
            || !common.TryGetProperty(name, out var arr)
            || arr.ValueKind != JsonValueKind.Array)
        {
            return (string.Empty, null);
        }
        foreach (var entry in arr.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.String) continue;
            var raw = entry.GetString();
            if (string.IsNullOrWhiteSpace(raw)) continue;

            try
            {
                var address = new MailAddress(raw);
                return (address.Address, string.IsNullOrEmpty(address.DisplayName) ? null : address.DisplayName);
            }
            catch (FormatException)
            {
                // Not parseable as RFC 5322 — return the raw string
                // so downstream callers have something to work with.
                return (raw.Trim(), null);
            }
        }
        return (string.Empty, null);
    }

    /// <summary>
    /// Flatten the <c>mail.headers</c> array into a case-sensitive
    /// Dictionary. SES presents each entry as
    /// <c>{"name": "...", "value": "..."}</c>.
    /// </summary>
    private static Dictionary<string, string> ExtractHeaders(JsonElement mail)
    {
        var map = new Dictionary<string, string>();
        if (mail.ValueKind != JsonValueKind.Object
            || !mail.TryGetProperty("headers", out var arr)
            || arr.ValueKind != JsonValueKind.Array)
        {
            return map;
        }
        foreach (var entry in arr.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;
            var name = TryGetString(entry, "name");
            var value = TryGetString(entry, "value");
            if (!string.IsNullOrEmpty(name) && value is not null)
            {
                map[name] = value;
            }
        }
        return map;
    }

    /// <summary>
    /// Decode the base64 <c>content</c> field (populated when the
    /// SES receipt rule is configured with action.type=SNS +
    /// encoding=BASE64) and extract text/plain + text/html bodies.
    /// Returns <c>(null, null)</c> when the field is absent,
    /// malformed, or the MIME parse fails.
    /// </summary>
    private static (string? Text, string? Html) ExtractBody(JsonElement notification)
    {
        var contentB64 = TryGetString(notification, "content");
        if (string.IsNullOrEmpty(contentB64))
        {
            return (null, null);
        }

        byte[] raw;
        try
        {
            raw = Convert.FromBase64String(contentB64);
        }
        catch (FormatException)
        {
            return (null, null);
        }

        try
        {
            var mime = new MimeMessageParser(raw);
            return mime.ExtractBodies();
        }
        catch
        {
            return (null, null);
        }
    }

    /// <summary>
    /// Minimal RFC 822 / 2045 multipart decoder for SES message
    /// bodies. Only recognises what we need: <c>text/plain</c>,
    /// <c>text/html</c>, and <c>multipart/*</c> with quoted-printable
    /// transfer encoding. Unknown content-types are treated as
    /// text/plain.
    /// </summary>
    private sealed class MimeMessageParser
    {
        private readonly byte[] _raw;

        public MimeMessageParser(byte[] raw) { _raw = raw; }

        public (string? Text, string? Html) ExtractBodies()
        {
            var (headers, bodyStart) = ReadHeaderBlock(_raw, 0);
            var contentTypeHeader = headers.GetValueOrDefault("content-type", "text/plain");
            var transferEnc = headers.GetValueOrDefault("content-transfer-encoding", "7bit");

            var ct = new ContentType(contentTypeHeader);
            if (!ct.MediaType.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase))
            {
                var body = DecodeBody(_raw, bodyStart, _raw.Length, transferEnc);
                return ct.MediaType.Equals("text/html", StringComparison.OrdinalIgnoreCase)
                    ? (null, body)
                    : (body, null);
            }

            var boundary = ct.Boundary;
            if (string.IsNullOrEmpty(boundary))
            {
                return (null, null);
            }

            string? text = null, html = null;
            foreach (var (partStart, partEnd) in SplitMultipartBody(_raw, bodyStart, _raw.Length, boundary))
            {
                var (partHeaders, partBodyStart) = ReadHeaderBlock(_raw, partStart);
                var partCt = partHeaders.GetValueOrDefault("content-type", "text/plain");
                var partEnc = partHeaders.GetValueOrDefault("content-transfer-encoding", "7bit");
                var partBody = DecodeBody(_raw, partBodyStart, partEnd, partEnc);

                var media = new ContentType(partCt).MediaType;
                if (media.Equals("text/plain", StringComparison.OrdinalIgnoreCase) && text is null)
                {
                    text = partBody;
                }
                else if (media.Equals("text/html", StringComparison.OrdinalIgnoreCase) && html is null)
                {
                    html = partBody;
                }
            }
            return (text, html);
        }

        private static (Dictionary<string, string> Headers, int BodyStart) ReadHeaderBlock(byte[] raw, int start)
        {
            // Headers end at the first blank line (\r\n\r\n or \n\n).
            var bodyStart = IndexOf(raw, start, raw.Length, new byte[] { (byte)'\r', (byte)'\n', (byte)'\r', (byte)'\n' });
            int skip = 4;
            if (bodyStart == -1)
            {
                bodyStart = IndexOf(raw, start, raw.Length, new byte[] { (byte)'\n', (byte)'\n' });
                skip = 2;
            }
            if (bodyStart == -1)
            {
                return (new Dictionary<string, string>(), raw.Length);
            }
            var headerBlock = Encoding.UTF8.GetString(raw, start, bodyStart - start);
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in headerBlock.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
            {
                var colon = line.IndexOf(':');
                if (colon <= 0) continue;
                var name = line[..colon].Trim();
                var value = line[(colon + 1)..].Trim();
                headers[name] = value;
            }
            return (headers, bodyStart + skip);
        }

        private static IEnumerable<(int Start, int End)> SplitMultipartBody(byte[] raw, int start, int end, string boundary)
        {
            var delimiter = Encoding.UTF8.GetBytes("--" + boundary);
            int cursor = start;
            int? partStart = null;
            while (cursor < end)
            {
                var next = IndexOf(raw, cursor, end, delimiter);
                if (next == -1) break;

                if (partStart.HasValue)
                {
                    // The line break that preceded the delimiter is not
                    // part of the body content.
                    int partEnd = next;
                    while (partEnd > partStart.Value && (raw[partEnd - 1] == '\r' || raw[partEnd - 1] == '\n'))
                    {
                        partEnd--;
                    }
                    yield return (partStart.Value, partEnd);
                }

                cursor = next + delimiter.Length;
                // Closing delimiter is "--boundary--".
                if (cursor + 2 <= end && raw[cursor] == '-' && raw[cursor + 1] == '-')
                {
                    yield break;
                }
                // Skip the CRLF after the delimiter.
                while (cursor < end && (raw[cursor] == '\r' || raw[cursor] == '\n'))
                {
                    cursor++;
                }
                partStart = cursor;
            }
        }

        private static string DecodeBody(byte[] raw, int start, int end, string transferEnc)
        {
            var length = Math.Max(0, end - start);
            if (length == 0) return string.Empty;
            var bytes = new byte[length];
            Buffer.BlockCopy(raw, start, bytes, 0, length);
            if (transferEnc.Equals("quoted-printable", StringComparison.OrdinalIgnoreCase))
            {
                return DecodeQuotedPrintable(Encoding.UTF8.GetString(bytes));
            }
            if (transferEnc.Equals("base64", StringComparison.OrdinalIgnoreCase))
            {
                try { return Encoding.UTF8.GetString(Convert.FromBase64String(Encoding.UTF8.GetString(bytes))); }
                catch { return Encoding.UTF8.GetString(bytes); }
            }
            return Encoding.UTF8.GetString(bytes);
        }

        private static string DecodeQuotedPrintable(string input)
        {
            var sb = new StringBuilder(input.Length);
            for (int i = 0; i < input.Length; i++)
            {
                if (input[i] == '=' && i + 2 < input.Length)
                {
                    if (input[i + 1] == '\r' && input[i + 2] == '\n') { i += 2; continue; }
                    if (input[i + 1] == '\n') { i += 1; continue; }
                    if (TryParseHex(input[i + 1], input[i + 2], out var b))
                    {
                        sb.Append((char)b);
                        i += 2;
                        continue;
                    }
                }
                sb.Append(input[i]);
            }
            return sb.ToString();
        }

        private static bool TryParseHex(char a, char b, out byte value)
        {
            value = 0;
            var hex = $"{a}{b}";
            return byte.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out value);
        }

        private static int IndexOf(byte[] haystack, int start, int end, byte[] needle)
        {
            for (int i = start; i + needle.Length <= end; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j]) { match = false; break; }
                }
                if (match) return i;
            }
            return -1;
        }
    }
}

/// <summary>
/// Thrown by <see cref="SESInboundParser.ParseAsync"/> when the
/// webhook receives an SNS subscription-confirmation envelope. The
/// host app must fetch <see cref="SubscribeUrl"/> out-of-band to
/// activate the subscription.
/// </summary>
public class SESSubscriptionConfirmationException : Exception
{
    public string TopicArn { get; }
    public string SubscribeUrl { get; }
    public string Token { get; }

    public SESSubscriptionConfirmationException(string topicArn, string subscribeUrl, string token)
        : base($"SES subscription confirmation for topic {topicArn}; GET {subscribeUrl} to confirm.")
    {
        TopicArn = topicArn;
        SubscribeUrl = subscribeUrl;
        Token = token;
    }
}
