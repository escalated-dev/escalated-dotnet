using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace Escalated.Controllers.Newsletter;

internal static class NewsletterHttp
{
    public static IActionResult Inertia(ControllerBase controller, string component, object props) =>
        controller.Ok(new { component, props });

    public static IActionResult Redirect(ControllerBase controller, string url, object? extra = null) =>
        controller.Ok(extra is null ? new { redirect = url } : Merge(new { redirect = url }, extra));

    public static string? OptionalString(JsonElement body, string key, int? max = null)
    {
        if (!body.TryGetProperty(key, out var value) || value.ValueKind is JsonValueKind.Null)
            return null;
        var text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
        if (string.IsNullOrEmpty(text)) return null;
        if (max is not null && text.Length > max) throw new InvalidOperationException($"{key} may not be greater than {max} characters");
        return text;
    }

    public static string RequiredString(JsonElement body, string key, int? max = null)
    {
        var value = OptionalString(body, key, max);
        if (string.IsNullOrEmpty(value)) throw new InvalidOperationException($"{key} is required");
        return value;
    }

    public static int RequiredInt(JsonElement body, string key, int? min = null, int? max = null)
    {
        if (!body.TryGetProperty(key, out var value) || !value.TryGetInt32(out var number))
            throw new InvalidOperationException($"{key} must be an integer");
        if (min is not null && number < min) throw new InvalidOperationException($"{key} must be at least {min}");
        if (max is not null && number > max) throw new InvalidOperationException($"{key} must be at most {max}");
        return number;
    }

    public static int? OptionalInt(JsonElement body, string key)
    {
        if (!body.TryGetProperty(key, out var value) || value.ValueKind is JsonValueKind.Null)
            return null;
        if (value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString()))
            return null;
        return value.TryGetInt32(out var number) ? number : throw new InvalidOperationException($"{key} must be an integer");
    }

    public static bool RequiredBool(JsonElement body, string key)
    {
        if (!body.TryGetProperty(key, out var value))
            throw new InvalidOperationException($"{key} must be a boolean");
        if (value.ValueKind == JsonValueKind.True) return true;
        if (value.ValueKind == JsonValueKind.False) return false;
        if (value.ValueKind == JsonValueKind.String)
        {
            var raw = value.GetString();
            if (raw is "1" or "true") return true;
            if (raw is "0" or "false") return false;
        }
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            return number != 0;
        throw new InvalidOperationException($"{key} must be a boolean");
    }

    public static string? Email(string? value, string key, bool required = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required) throw new InvalidOperationException($"{key} must be a valid email");
            return null;
        }

        if (!value.Contains('@') || value.Length > 320)
            throw new InvalidOperationException($"{key} must be a valid email");
        return value;
    }

    public static T OneOf<T>(string? value, string key, params T[] allowed) where T : notnull
    {
        foreach (var item in allowed)
        {
            if (string.Equals(value, item.ToString(), StringComparison.Ordinal))
                return item;
        }

        throw new InvalidOperationException($"{key} must be one of {string.Join(", ", allowed)}");
    }

    public static DateTime? OptionalFutureDate(JsonElement body, string key)
    {
        var raw = OptionalString(body, key);
        if (raw is null) return null;
        if (!DateTime.TryParse(raw, out var date) || date <= DateTime.UtcNow)
            throw new InvalidOperationException($"{key} must be a future date");
        return date.ToUniversalTime();
    }

    public static string? RawJson(JsonElement body, string key)
    {
        if (!body.TryGetProperty(key, out var value) || value.ValueKind is JsonValueKind.Null)
            return null;
        if (value.ValueKind is not (JsonValueKind.Array or JsonValueKind.Object))
            throw new InvalidOperationException($"{key} must be an array");
        return value.GetRawText();
    }

    public static string DecodeTrackedUrl(string encoded)
    {
        try
        {
            var normalized = encoded.Replace('-', '+').Replace('_', '/');
            normalized = normalized.PadRight(normalized.Length + ((4 - normalized.Length % 4) % 4), '=');
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(normalized));
            var uri = new Uri(decoded, UriKind.Absolute);
            if (uri.Scheme is not ("http" or "https"))
                throw new InvalidOperationException();
            return decoded;
        }
        catch
        {
            throw new InvalidOperationException("Bad request");
        }
    }

    public static string Escape(string value) => WebUtility.HtmlEncode(value);

    private static object Merge(object first, object second)
    {
        var dict = first.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(first));
        foreach (var prop in second.GetType().GetProperties())
            dict[prop.Name] = prop.GetValue(second);
        return dict;
    }
}
