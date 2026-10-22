using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Escalated.Ui;

/// <summary>
/// A page form's body. Inertia posts JSON, or multipart form data when the form
/// holds files (a reply with attachments), so both read the same way here.
/// Arrays arrive as JSON arrays, or as repeated <c>key[]</c> / <c>key[0]</c> fields.
/// </summary>
public sealed class UiInput
{
    private readonly Dictionary<string, List<string?>> _values;
    private readonly Dictionary<string, JsonElement> _json;
    private readonly IFormCollection? _form;

    private UiInput(Dictionary<string, List<string?>> values, IFormFileCollection? files, Dictionary<string, JsonElement>? json = null,
        IFormCollection? form = null)
    {
        _values = values;
        Files = files;
        _json = json ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        _form = form;
    }

    public IFormFileCollection? Files { get; }

    public static async Task<UiInput> ReadAsync(HttpRequest request, CancellationToken ct = default)
    {
        var values = new Dictionary<string, List<string?>>(StringComparer.Ordinal);

        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync(ct);
            foreach (var (key, value) in form)
            {
                var name = NormalizeKey(key);
                if (!values.TryGetValue(name, out var list)) values[name] = list = new List<string?>();
                list.AddRange(value.Select(v => (string?)v));
            }

            return new UiInput(values, form.Files, form: form);
        }

        if (request.ContentLength is 0 || request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) != true)
        {
            return new UiInput(values, null);
        }

        var json = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        try
        {
            using var document = await JsonDocument.ParseAsync(request.Body, cancellationToken: ct);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    json[property.Name] = property.Value.Clone();
                    values[property.Name] = property.Value.ValueKind == JsonValueKind.Array
                        ? property.Value.EnumerateArray().Select(Scalar).ToList()
                        : new List<string?> { Scalar(property.Value) };
                }
            }
        }
        catch (JsonException)
        {
            // An unreadable body reads as an empty form, and validation reports what is missing.
        }

        return new UiInput(values, null, json);
    }

    public bool Has(string key) => _values.ContainsKey(key);

    /// <summary>
    /// A JSON body's value as sent, for nested input (an array of objects, an object
    /// keyed by priority). Null when the key is absent or the body was a form.
    /// </summary>
    public JsonElement? Json(string key) => _json.TryGetValue(key, out var value) ? value : null;

    /// <summary>
    /// An object field (<c>custom_fields</c>): the JSON value as sent, or, from form data,
    /// an object built from <c>key[name]</c> (a string) and <c>key[name][]</c> (an array)
    /// fields. Null when the key is absent.
    /// </summary>
    public JsonElement? Object(string key)
    {
        if (_form is null) return Json(key);

        var map = new Dictionary<string, object?>(StringComparer.Ordinal);
        var prefix = key + "[";
        foreach (var (name, value) in _form)
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var close = name.IndexOf(']', prefix.Length);
            if (close < 0) continue;
            var field = name[prefix.Length..close];
            var rest = name[(close + 1)..];
            if (rest.Length > 0)
            {
                var list = map.GetValueOrDefault(field) as List<string?> ?? new List<string?>();
                list.AddRange(value.Select(v => (string?)v));
                map[field] = list;
            }
            else
            {
                map[field] = value.ToString();
            }
        }

        if (map.Count == 0) return _form.ContainsKey(key) ? JsonSerializer.SerializeToElement(_form[key].ToString()) : null;
        return JsonSerializer.SerializeToElement(map);
    }

    public string? String(string key)
    {
        var value = _values.GetValueOrDefault(key)?.FirstOrDefault();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public int? Int(string key) => int.TryParse(String(key), out var number) ? number : null;

    public bool Bool(string key) => String(key)?.ToLowerInvariant() is "1" or "true" or "on" or "yes";

    public List<string> List(string key) =>
        (_values.GetValueOrDefault(key) ?? new List<string?>()).Where(v => !string.IsNullOrEmpty(v)).Select(v => v!).ToList();

    public List<int>? IntList(string key)
    {
        var raw = List(key);
        var numbers = new List<int>();
        foreach (var item in raw)
        {
            if (!int.TryParse(item, out var number)) return null;
            numbers.Add(number);
        }

        return numbers;
    }

    private static string NormalizeKey(string key)
    {
        var bracket = key.IndexOf('[');
        return bracket > 0 ? key[..bracket] : key;
    }

    private static string? Scalar(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => value.GetRawText(),
    };
}
