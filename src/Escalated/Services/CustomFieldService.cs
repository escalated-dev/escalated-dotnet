using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Escalated.Data;
using Escalated.Models;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Services;

/// <summary>
/// Custom field values on tickets: the definitions a ticket form shows, the
/// validation of a <c>custom_fields</c> object keyed by field slug, and storage in
/// <c>custom_field_values</c> (<c>entity_type = "ticket"</c>). Only active fields with
/// the <c>ticket</c> context count.
///
/// <para>Stored encoding: <c>multi_select</c> as a JSON array string, <c>checkbox</c>
/// as <c>"1"</c>/<c>"0"</c>, everything else as the string given. Read back decoded:
/// arrays, booleans and numbers.</para>
/// </summary>
public sealed class CustomFieldService
{
    public const string TicketEntity = "ticket";

    public static readonly IReadOnlyList<string> Types = new[] { "text", "textarea", "select", "multi_select", "checkbox", "date", "number" };

    public static readonly IReadOnlyList<string> Contexts = new[] { "ticket", "user", "organization" };

    private readonly EscalatedDbContext _db;

    public CustomFieldService(EscalatedDbContext db)
    {
        _db = db;
    }

    /// <summary>The active ticket fields, in position order.</summary>
    public Task<List<CustomField>> TicketFieldsAsync(CancellationToken ct = default) =>
        _db.CustomFields.AsNoTracking()
            .Where(f => f.Active && f.Context == TicketEntity)
            .OrderBy(f => f.Position).ThenBy(f => f.Id)
            .ToListAsync(ct);

    /// <summary>The ticket forms' definitions: <c>{id, name, slug, type, options, required, placeholder, description, position}</c>.</summary>
    public async Task<List<Dictionary<string, object?>>> TicketDefinitionsAsync(CancellationToken ct = default) =>
        (await TicketFieldsAsync(ct)).Select(Definition).ToList();

    /// <summary>The definitions plus each field's decoded <c>value</c> on the ticket, null when unset.</summary>
    public async Task<List<Dictionary<string, object?>>> TicketValuesAsync(int ticketId, CancellationToken ct = default)
    {
        var fields = await TicketFieldsAsync(ct);
        var ids = fields.Select(f => f.Id).ToList();
        var stored = await _db.CustomFieldValues.AsNoTracking()
            .Where(v => v.EntityType == TicketEntity && v.EntityId == ticketId && ids.Contains(v.CustomFieldId))
            .ToDictionaryAsync(v => v.CustomFieldId, v => v.Value, ct);

        return fields.Select(field =>
        {
            var item = Definition(field);
            item["value"] = Decode(field, stored.GetValueOrDefault(field.Id));
            return item;
        }).ToList();
    }

    public static Dictionary<string, object?> Definition(CustomField field) => new()
    {
        ["id"] = field.Id,
        ["name"] = field.Name,
        ["slug"] = field.Slug,
        ["type"] = field.Type,
        ["options"] = Options(field),
        ["required"] = field.Required,
        ["placeholder"] = field.Placeholder,
        ["description"] = field.Description,
        ["position"] = field.Position,
    };

    /// <summary>The field's options as a list of strings; null when it has none.</summary>
    public static List<string>? Options(CustomField field)
    {
        if (string.IsNullOrWhiteSpace(field.Options)) return null;
        try
        {
            using var document = JsonDocument.Parse(field.Options);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return null;
            return document.RootElement.EnumerateArray()
                .Select(o => o.ValueKind == JsonValueKind.String ? o.GetString()! : o.GetRawText())
                .ToList();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>A stored value decoded for the page or API.</summary>
    public static object? Decode(CustomField field, string? value)
    {
        if (value is null) return null;
        switch (field.Type)
        {
            case "multi_select":
                try
                {
                    using var document = JsonDocument.Parse(value);
                    return document.RootElement.ValueKind == JsonValueKind.Array
                        ? document.RootElement.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.GetRawText()).ToList()
                        : new List<string?> { value };
                }
                catch (JsonException)
                {
                    return new List<string?> { value };
                }
            case "checkbox":
                return value is "1" or "true";
            case "number":
                return decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : value;
            default:
                return value;
        }
    }

    /// <summary>
    /// Checks a <c>custom_fields</c> object and returns what to store per field id
    /// (null deletes the value). On create every required field must be present and
    /// non-empty; on update only the keys sent are checked. Errors are keyed
    /// <c>custom_fields.&lt;slug&gt;</c>.
    /// </summary>
    public async Task<CustomFieldPlan> ValidateTicketAsync(JsonElement? input, bool creating, CancellationToken ct = default)
    {
        var plan = new CustomFieldPlan();
        if (input is null || input.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            if (creating) await RequireAllAsync(plan, new HashSet<string>(), ct);
            return plan;
        }

        if (input.Value.ValueKind != JsonValueKind.Object)
        {
            plan.Errors["custom_fields"] = "The custom fields must be an object keyed by field slug.";
            return plan;
        }

        var fields = (await TicketFieldsAsync(ct)).ToDictionary(f => f.Slug, StringComparer.Ordinal);
        var sent = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in input.Value.EnumerateObject())
        {
            sent.Add(property.Name);
            var key = $"custom_fields.{property.Name}";
            if (!fields.TryGetValue(property.Name, out var field))
            {
                plan.Errors[key] = $"The custom field \"{property.Name}\" does not exist.";
                continue;
            }

            if (Validate(field, property.Value, out var encoded) is { } error)
            {
                plan.Errors[key] = error;
                continue;
            }

            if (encoded is null && field.Required)
            {
                plan.Errors[key] = $"The {field.Name} field is required.";
                continue;
            }

            plan.Values[field.Id] = encoded;
        }

        if (creating) await RequireAllAsync(plan, sent, ct, fields.Values);
        return plan;
    }

    private async Task RequireAllAsync(CustomFieldPlan plan, HashSet<string> sent, CancellationToken ct, IEnumerable<CustomField>? fields = null)
    {
        foreach (var field in fields ?? await TicketFieldsAsync(ct))
        {
            if (field.Required && !sent.Contains(field.Slug)) plan.Errors[$"custom_fields.{field.Slug}"] = $"The {field.Name} field is required.";
        }
    }

    /// <summary>One value checked against its field; <paramref name="encoded"/> is null for an empty value.</summary>
    private static string? Validate(CustomField field, JsonElement value, out string? encoded)
    {
        encoded = null;
        if (IsEmpty(value)) return null;

        switch (field.Type)
        {
            case "number":
                {
                    var raw = value.ValueKind switch
                    {
                        JsonValueKind.Number => value.GetRawText(),
                        JsonValueKind.String => value.GetString()!.Trim(),
                        _ => null,
                    };
                    if (raw is null || !decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                        return $"The {field.Name} field must be a number.";
                    encoded = raw;
                    return null;
                }
            case "date":
                {
                    if (value.ValueKind != JsonValueKind.String
                        || !DateTime.TryParseExact(value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                        return $"The {field.Name} field must be a valid date (YYYY-MM-DD).";
                    encoded = value.GetString();
                    return null;
                }
            case "checkbox":
                {
                    bool? flag = value.ValueKind switch
                    {
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        JsonValueKind.Number => value.GetRawText() switch { "1" => true, "0" => false, _ => null },
                        JsonValueKind.String => value.GetString()!.Trim().ToLowerInvariant() switch
                        {
                            "1" or "true" => true,
                            "0" or "false" => false,
                            _ => null,
                        },
                        _ => null,
                    };
                    if (flag is null) return $"The {field.Name} field must be true or false.";
                    encoded = flag.Value ? "1" : "0";
                    return null;
                }
            case "select":
                {
                    var options = Options(field) ?? new List<string>();
                    if (value.ValueKind != JsonValueKind.String || !options.Contains(value.GetString()!))
                        return $"The selected {field.Name} is invalid.";
                    encoded = value.GetString();
                    return null;
                }
            case "multi_select":
                {
                    var options = Options(field) ?? new List<string>();
                    if (value.ValueKind != JsonValueKind.Array) return $"The {field.Name} field must be a list.";
                    var picked = new List<string>();
                    foreach (var item in value.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.String || !options.Contains(item.GetString()!))
                            return $"The selected {field.Name} is invalid.";
                        picked.Add(item.GetString()!);
                    }

                    encoded = JsonSerializer.Serialize(picked);
                    return null;
                }
            case "text":
                {
                    if (value.ValueKind != JsonValueKind.String) return $"The {field.Name} field must be a string.";
                    if (value.GetString()!.Length > 255) return $"The {field.Name} field may not be greater than 255 characters.";
                    encoded = value.GetString();
                    return null;
                }
            default:
                {
                    if (value.ValueKind != JsonValueKind.String) return $"The {field.Name} field must be a string.";
                    encoded = value.GetString();
                    return null;
                }
        }
    }

    private static bool IsEmpty(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => true,
        JsonValueKind.String => string.IsNullOrWhiteSpace(value.GetString()),
        JsonValueKind.Array => value.GetArrayLength() == 0,
        _ => false,
    };

    /// <summary>Writes a validated plan onto the ticket: upserts set values, deletes cleared ones.</summary>
    public async Task ApplyTicketAsync(int ticketId, CustomFieldPlan plan, CancellationToken ct = default)
    {
        if (plan.Values.Count == 0) return;

        var ids = plan.Values.Keys.ToList();
        var existing = await _db.CustomFieldValues
            .Where(v => v.EntityType == TicketEntity && v.EntityId == ticketId && ids.Contains(v.CustomFieldId))
            .ToListAsync(ct);

        foreach (var (fieldId, value) in plan.Values)
        {
            var row = existing.FirstOrDefault(v => v.CustomFieldId == fieldId);
            if (value is null)
            {
                if (row is not null) _db.CustomFieldValues.Remove(row);
                continue;
            }

            if (row is null)
            {
                row = new CustomFieldValue { CustomFieldId = fieldId, EntityType = TicketEntity, EntityId = ticketId, CreatedAt = DateTime.UtcNow };
                _db.CustomFieldValues.Add(row);
                // The ticket's own foreign key, kept alongside the polymorphic pair.
                _db.Entry(row).Property("TicketId").CurrentValue = ticketId;
            }

            row.Value = value;
            row.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// A slug from the name, <c>_</c>-separated (Laravel's <c>Str::slug($name, '_')</c>),
    /// made unique with <c>_2</c>, <c>_3</c>...
    /// </summary>
    public async Task<string> UniqueSlugAsync(string name, int? exceptId = null, CancellationToken ct = default)
    {
        var slug = Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');
        if (slug.Length == 0) slug = "field";
        if (slug.Length > 90) slug = slug[..90].TrimEnd('_');

        var taken = await _db.CustomFields.AsNoTracking()
            .Where(f => (f.Slug == slug || f.Slug.StartsWith(slug + "_")) && f.Id != exceptId)
            .Select(f => f.Slug).ToListAsync(ct);
        if (!taken.Contains(slug)) return slug;

        var n = 2;
        while (taken.Contains($"{slug}_{n}")) n++;
        return $"{slug}_{n}";
    }
}

/// <summary>A validated <c>custom_fields</c> input: values to write per field id (null clears), or errors.</summary>
public sealed class CustomFieldPlan
{
    public Dictionary<int, string?> Values { get; } = new();

    public Dictionary<string, string> Errors { get; } = new(StringComparer.Ordinal);

    public bool IsValid => Errors.Count == 0;
}
