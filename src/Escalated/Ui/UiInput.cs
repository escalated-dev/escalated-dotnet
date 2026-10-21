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

    private UiInput(Dictionary<string, List<string?>> values, IFormFileCollection? files)
    {
        _values = values;
        Files = files;
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

            return new UiInput(values, form.Files);
        }

        if (request.ContentLength is 0 || request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) != true)
        {
            return new UiInput(values, null);
        }

        try
        {
            using var document = await JsonDocument.ParseAsync(request.Body, cancellationToken: ct);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in document.RootElement.EnumerateObject())
                {
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

        return new UiInput(values, null);
    }

    public bool Has(string key) => _values.ContainsKey(key);

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
