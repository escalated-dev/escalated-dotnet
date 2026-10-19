using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Escalated.Widget;

/// <summary>
/// Host parameters an embedding page sends with a widget ticket or chat
/// (order number, plan, account id, the page URL, ...). They are visitor
/// supplied, so only a flat map of short scalar values is kept.
/// </summary>
public static partial class WidgetMetadata
{
    public const int MaxEntries = 25;
    public const int MaxKeys = 50;
    public const int MaxValueLength = 500;
    public const int MaxPageUrlLength = 2048;

    [GeneratedRegex(@"^[A-Za-z0-9_.:-]{1,64}\z")]
    private static partial Regex KeyRegex();

    /// <summary>
    /// Null when <paramref name="metadata"/> is absent, null or an object of at
    /// most <see cref="MaxKeys"/> keys; otherwise the validation message.
    /// </summary>
    public static string? Validate(JsonElement? metadata)
    {
        if (metadata is not { } value || value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            return "The metadata field must be an object.";
        }

        return value.EnumerateObject().Count() > MaxKeys
            ? $"The metadata field must not have more than {MaxKeys} items."
            : null;
    }

    /// <summary>
    /// The kept entries as a JSON object, or null when none are left.
    /// <paramref name="embeddedOn"/> is the allowed other-site origin the
    /// request came from; a client-sent <c>embedded_on</c> is always dropped.
    /// </summary>
    public static string? Sanitize(JsonElement? metadata, string? embeddedOn)
    {
        // Ordered like the request; a repeated key keeps its first position and
        // its last value. Values are a string or a number/boolean JsonElement.
        var keys = new List<string>();
        var values = new Dictionary<string, object>();

        if (metadata is { ValueKind: JsonValueKind.Object } value)
        {
            foreach (var property in value.EnumerateObject())
            {
                if (!values.ContainsKey(property.Name) && values.Count >= MaxEntries)
                {
                    break;
                }

                if (!KeyRegex().IsMatch(property.Name))
                {
                    continue;
                }

                object? kept = property.Value.ValueKind switch
                {
                    JsonValueKind.String => Truncate(property.Value.GetString()!.Trim(), MaxValueLength),
                    JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => property.Value,
                    _ => null,
                };
                if (kept is null)
                {
                    continue;
                }

                if (!values.ContainsKey(property.Name))
                {
                    keys.Add(property.Name);
                }

                values[property.Name] = kept;
            }
        }

        if (values.TryGetValue("page_url", out var pageUrl) && !(pageUrl is string url && IsWebUrl(url)))
        {
            Remove("page_url");
        }

        // Recorded by the server from the request's Origin header, so the page
        // cannot claim another site.
        Remove("embedded_on");
        if (embeddedOn is not null)
        {
            keys.Add("embedded_on");
            values["embedded_on"] = embeddedOn;
        }

        if (keys.Count == 0)
        {
            return null;
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var key in keys)
            {
                writer.WritePropertyName(key);
                if (values[key] is string text)
                {
                    writer.WriteStringValue(text);
                }
                else
                {
                    ((JsonElement)values[key]).WriteTo(writer);
                }
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());

        void Remove(string key)
        {
            keys.Remove(key);
            values.Remove(key);
        }
    }

    private static bool IsWebUrl(string value) =>
        value.Length <= MaxPageUrlLength
        && Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && !string.IsNullOrEmpty(uri.Host);

    /// <summary>The first <paramref name="max"/> characters, never splitting a surrogate pair.</summary>
    private static string Truncate(string value, int max)
    {
        var builder = new StringBuilder();
        var count = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            if (count++ >= max)
            {
                break;
            }

            builder.Append(rune.ToString());
        }

        return builder.ToString();
    }
}
