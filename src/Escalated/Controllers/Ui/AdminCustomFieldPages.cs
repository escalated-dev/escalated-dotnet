using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Escalated.Models;
using Escalated.Services;
using Escalated.Ui;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Controllers.Ui;

/// <summary>
/// Custom fields and custom objects, with escalated-laravel's routes and props
/// (<c>Admin/CustomFieldController</c>, <c>Admin/CustomObjectController</c>):
/// <c>CustomFields/Index</c> and <c>Form</c>, <c>CustomObjects/Index</c>, <c>Form</c>
/// and <c>Records</c>. The custom field list and create share a path with the JSON
/// admin API, so those two are negotiated and API clients are unchanged.
/// </summary>
public partial class AdminPageController
{
    private static readonly IReadOnlyList<string> ObjectFieldTypes = new[] { "text", "number", "select", "date", "lookup" };

    private static readonly Regex AlphaDash = new("^[A-Za-z0-9_-]+$", RegexOptions.Compiled);

    // --- Custom fields ---

    [HttpGet("custom-fields")]
    [EscalatedPage(Negotiate = true)]
    public async Task<IActionResult> CustomFields(CancellationToken ct)
    {
        var fields = await _db.CustomFields.AsNoTracking().OrderBy(f => f.Position).ThenBy(f => f.Id).ToListAsync(ct);
        return UiResults.Render("Escalated/Admin/CustomFields/Index", new { fields = fields.Select(CustomField).ToList() });
    }

    [HttpGet("custom-fields/create")]
    public IActionResult CreateCustomField() =>
        UiResults.Render("Escalated/Admin/CustomFields/Form", new Dictionary<string, object?>
        {
            ["field"] = null,
            ["contexts"] = CustomFieldService.Contexts,
        });

    [HttpGet("custom-fields/{id:int}/edit")]
    public async Task<IActionResult> EditCustomField(int id, CancellationToken ct)
    {
        var field = await _db.CustomFields.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
        if (field is null) return NotFound();
        return UiResults.Render("Escalated/Admin/CustomFields/Form", new Dictionary<string, object?>
        {
            ["field"] = CustomField(field),
            ["contexts"] = CustomFieldService.Contexts,
        });
    }

    private static Dictionary<string, object?> CustomField(CustomField field) => new()
    {
        ["id"] = field.Id,
        ["name"] = field.Name,
        ["slug"] = field.Slug,
        ["type"] = field.Type,
        ["context"] = field.Context,
        ["options"] = CustomFieldService.Options(field),
        ["required"] = field.Required,
        ["placeholder"] = field.Placeholder,
        ["description"] = field.Description,
        ["validation_rules"] = UiPresenter.ParseJson(field.ValidationRules),
        ["conditions"] = UiPresenter.ParseJson(field.Conditions),
        ["position"] = field.Position,
        ["active"] = field.Active,
        ["created_at"] = field.CreatedAt,
        ["updated_at"] = field.UpdatedAt,
    };

    [HttpPost("custom-fields")]
    [EscalatedPage(Negotiate = true)]
    public Task<IActionResult> StoreCustomField(CancellationToken ct) => SaveCustomFieldAsync(null, ct);

    [HttpPut("custom-fields/{id:int}")]
    [HttpPatch("custom-fields/{id:int}")]
    public Task<IActionResult> UpdateCustomField(int id, CancellationToken ct) => SaveCustomFieldAsync(id, ct);

    private async Task<IActionResult> SaveCustomFieldAsync(int? id, CancellationToken ct)
    {
        var field = id is null ? new CustomField { CreatedAt = DateTime.UtcNow } : await _db.CustomFields.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (field is null) return NotFound();

        var input = await UiInput.ReadAsync(Request, ct);
        var errors = Errors();

        var name = input.String("name");
        if (name is null) errors["name"] = "The name field is required.";
        else if (name.Length > 255) errors["name"] = "The name may not be greater than 255 characters.";

        var type = input.String("type");
        if (type is null) errors["type"] = "The type field is required.";
        else if (!CustomFieldService.Types.Contains(type)) errors["type"] = "The selected type is invalid.";

        var context = input.String("context");
        if (context is null) errors["context"] = "The context field is required.";
        else if (!CustomFieldService.Contexts.Contains(context)) errors["context"] = "The selected context is invalid.";

        List<string>? options = null;
        if (input.Json("options") is { } rawOptions && rawOptions.ValueKind is not JsonValueKind.Null)
        {
            if (rawOptions.ValueKind != JsonValueKind.Array || rawOptions.EnumerateArray().Any(o => o.ValueKind != JsonValueKind.String))
                errors["options"] = "The options must be a list of strings.";
            else
                options = rawOptions.EnumerateArray().Select(o => o.GetString()!.Trim()).Where(o => o.Length > 0).Distinct().ToList();
        }
        else if (input.Json("options") is null && input.List("options") is { Count: > 0 } formOptions)
        {
            options = formOptions.Select(o => o.Trim()).Where(o => o.Length > 0).Distinct().ToList();
        }

        if (type is "select" or "multi_select" && !errors.ContainsKey("options") && (options is null || options.Count == 0))
            errors["options"] = "A select field needs at least one option.";

        var placeholder = input.String("placeholder");
        if (placeholder is { Length: > 255 }) errors["placeholder"] = "The placeholder may not be greater than 255 characters.";

        string? validationRules = null;
        if (input.Json("validation_rules") is { } rules && rules.ValueKind is not JsonValueKind.Null)
        {
            if (rules.ValueKind is JsonValueKind.Array or JsonValueKind.Object) validationRules = rules.GetRawText();
            else errors["validation_rules"] = "The validation rules must be an array.";
        }

        int? position = null;
        if (input.String("position") is { } rawPosition)
        {
            if (int.TryParse(rawPosition, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) position = parsed;
            else errors["position"] = "The position must be an integer.";
        }

        var formUrl = id is null ? $"{Prefix}/admin/custom-fields/create" : $"{Prefix}/admin/custom-fields/{id}/edit";
        if (errors.Count > 0) return UiResults.Back(HttpContext, formUrl, errors: errors);

        if (id is null) field.Slug = await new CustomFieldService(_db).UniqueSlugAsync(name!, null, ct);
        field.Name = name!;
        field.Type = type!;
        field.Context = context!;
        field.Options = type is "select" or "multi_select" && options is not null ? JsonSerializer.Serialize(options) : null;
        field.Required = input.Bool("required");
        field.Placeholder = placeholder;
        field.Description = input.String("description");
        if (input.Has("validation_rules")) field.ValidationRules = validationRules;
        if (position is not null || id is null) field.Position = position ?? 0;
        field.Active = !input.Has("active") || input.Bool("active");
        field.UpdatedAt = DateTime.UtcNow;
        if (id is null) _db.CustomFields.Add(field);
        await _db.SaveChangesAsync(ct);

        return new UiRedirectResult($"{Prefix}/admin/custom-fields") { Success = id is null ? "Custom field created." : "Custom field updated." };
    }

    [HttpDelete("custom-fields/{id:int}")]
    public async Task<IActionResult> DestroyCustomField(int id, CancellationToken ct)
    {
        var field = await _db.CustomFields.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (field is null) return NotFound();

        // Values go with the field (ON DELETE CASCADE); removed here too so every provider agrees.
        await _db.CustomFieldValues.IgnoreQueryFilters().Where(v => v.CustomFieldId == id).ExecuteDeleteAsync(ct);
        _db.CustomFields.Remove(field);
        await _db.SaveChangesAsync(ct);

        return new UiRedirectResult($"{Prefix}/admin/custom-fields") { Success = "Custom field deleted." };
    }

    [HttpPost("custom-fields/reorder")]
    public async Task<IActionResult> ReorderCustomFields(CancellationToken ct)
    {
        var input = await UiInput.ReadAsync(Request, ct);
        var fallback = $"{Prefix}/admin/custom-fields";
        var pairs = new List<(int Id, int Position)>();
        if (input.Json("positions") is not { ValueKind: JsonValueKind.Array } positions || positions.GetArrayLength() == 0)
        {
            return UiResults.Back(HttpContext, fallback, errors: new Dictionary<string, string> { ["positions"] = "The positions field is required." });
        }

        var index = 0;
        foreach (var item in positions.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("id", out var idValue) || !TryInt(idValue, out var fieldId)
                || !item.TryGetProperty("position", out var positionValue) || !TryInt(positionValue, out var position))
            {
                return UiResults.Back(HttpContext, fallback,
                    errors: new Dictionary<string, string> { [$"positions.{index}"] = "Each position needs an integer id and position." });
            }

            pairs.Add((fieldId, position));
            index++;
        }

        var ids = pairs.Select(p => p.Id).Distinct().ToList();
        if (await _db.CustomFields.CountAsync(f => ids.Contains(f.Id), ct) != ids.Count)
        {
            return UiResults.Back(HttpContext, fallback, errors: new Dictionary<string, string> { ["positions"] = "The selected custom field is invalid." });
        }

        var fields = await _db.CustomFields.Where(f => ids.Contains(f.Id)).ToListAsync(ct);
        foreach (var (fieldId, position) in pairs)
        {
            var field = fields.First(f => f.Id == fieldId);
            field.Position = position;
            field.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        return UiResults.Back(HttpContext, fallback, success: "Fields reordered.");
    }

    private static bool TryInt(JsonElement value, out int number)
    {
        number = 0;
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetInt32(out number),
            JsonValueKind.String => int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number),
            _ => false,
        };
    }

    // --- Custom objects ---

    [HttpGet("custom-objects")]
    public async Task<IActionResult> CustomObjects(CancellationToken ct)
    {
        var rows = await _db.CustomObjects.AsNoTracking().OrderBy(o => o.Name).ThenBy(o => o.Id)
            .Select(o => new { Object = o, Records = _db.CustomObjectRecords.Count(r => r.ObjectId == o.Id) })
            .ToListAsync(ct);

        return UiResults.Render("Escalated/Admin/CustomObjects/Index", new
        {
            objects = rows.Select(row =>
            {
                var item = CustomObject(row.Object);
                item["records_count"] = row.Records;
                return item;
            }).ToList(),
        });
    }

    [HttpGet("custom-objects/create")]
    public IActionResult CreateCustomObject() =>
        UiResults.Render("Escalated/Admin/CustomObjects/Form", new Dictionary<string, object?> { ["object"] = null });

    [HttpGet("custom-objects/{id:int}/edit")]
    public async Task<IActionResult> EditCustomObject(int id, CancellationToken ct)
    {
        var obj = await _db.CustomObjects.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct);
        if (obj is null) return NotFound();
        return UiResults.Render("Escalated/Admin/CustomObjects/Form", new { @object = CustomObject(obj) });
    }

    private static Dictionary<string, object?> CustomObject(CustomObject obj) => new()
    {
        ["id"] = obj.Id,
        ["name"] = obj.Name,
        ["slug"] = obj.Slug,
        ["fields_schema"] = UiPresenter.ParseJson(obj.FieldsSchema) ?? Array.Empty<object>(),
        ["created_at"] = obj.CreatedAt,
        ["updated_at"] = obj.UpdatedAt,
    };

    private static Dictionary<string, object?> CustomObjectRecord(CustomObjectRecord record) => new()
    {
        ["id"] = record.Id,
        ["object_id"] = record.ObjectId,
        ["data"] = UiPresenter.ParseJson(record.Data) ?? new Dictionary<string, object?>(),
        ["created_at"] = record.CreatedAt,
        ["updated_at"] = record.UpdatedAt,
    };

    [HttpPost("custom-objects")]
    public Task<IActionResult> StoreCustomObject(CancellationToken ct) => SaveCustomObjectAsync(null, ct);

    [HttpPut("custom-objects/{id:int}")]
    [HttpPatch("custom-objects/{id:int}")]
    public Task<IActionResult> UpdateCustomObject(int id, CancellationToken ct) => SaveCustomObjectAsync(id, ct);

    private async Task<IActionResult> SaveCustomObjectAsync(int? id, CancellationToken ct)
    {
        var obj = id is null ? new CustomObject { CreatedAt = DateTime.UtcNow } : await _db.CustomObjects.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (obj is null) return NotFound();

        var input = await UiInput.ReadAsync(Request, ct);
        var errors = Errors();

        var name = input.String("name");
        if (name is null) errors["name"] = "The name field is required.";
        else if (name.Length > 255) errors["name"] = "The name may not be greater than 255 characters.";

        var slug = input.String("slug");
        if (slug is null) errors["slug"] = "The slug field is required.";
        else if (slug.Length > 100) errors["slug"] = "The slug may not be greater than 100 characters.";
        else if (!AlphaDash.IsMatch(slug)) errors["slug"] = "The slug may only contain letters, numbers, dashes and underscores.";
        else if (await _db.CustomObjects.AnyAsync(o => o.Slug == slug && o.Id != (id ?? 0), ct)) errors["slug"] = "The slug has already been taken.";

        var schema = new List<Dictionary<string, object?>>();
        if (input.Json("fields_schema") is not { ValueKind: JsonValueKind.Array } fields || fields.GetArrayLength() == 0)
        {
            errors["fields_schema"] = "The fields schema must have at least 1 field.";
        }
        else
        {
            var index = 0;
            foreach (var field in fields.EnumerateArray())
            {
                var key = $"fields_schema.{index++}";
                if (field.ValueKind != JsonValueKind.Object)
                {
                    errors[key] = "Each field must be an object.";
                    continue;
                }

                var fieldName = field.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()!.Trim() : null;
                if (string.IsNullOrEmpty(fieldName)) errors[$"{key}.name"] = "The field name is required.";
                else if (fieldName.Length > 100) errors[$"{key}.name"] = "The field name may not be greater than 100 characters.";

                var fieldType = field.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
                if (fieldType is null) errors[$"{key}.type"] = "The field type is required.";
                else if (!ObjectFieldTypes.Contains(fieldType)) errors[$"{key}.type"] = "The selected field type is invalid.";

                var entry = new Dictionary<string, object?> { ["name"] = fieldName, ["type"] = fieldType };
                if (field.TryGetProperty("required", out var required))
                {
                    if (required.ValueKind is JsonValueKind.True or JsonValueKind.False) entry["required"] = required.GetBoolean();
                    else errors[$"{key}.required"] = "The required flag must be true or false.";
                }

                if (field.TryGetProperty("options", out var opts) && opts.ValueKind != JsonValueKind.Null)
                {
                    if (opts.ValueKind == JsonValueKind.Array) entry["options"] = opts.Clone();
                    else errors[$"{key}.options"] = "The options must be a list.";
                }

                schema.Add(entry);
            }
        }

        var formUrl = id is null ? $"{Prefix}/admin/custom-objects/create" : $"{Prefix}/admin/custom-objects/{id}/edit";
        if (errors.Count > 0) return UiResults.Back(HttpContext, formUrl, errors: errors);

        obj.Name = name!;
        obj.Slug = slug!;
        obj.FieldsSchema = JsonSerializer.Serialize(schema);
        obj.UpdatedAt = DateTime.UtcNow;
        if (id is null) _db.CustomObjects.Add(obj);
        await _db.SaveChangesAsync(ct);

        return new UiRedirectResult($"{Prefix}/admin/custom-objects") { Success = id is null ? "Custom object created." : "Custom object updated." };
    }

    [HttpDelete("custom-objects/{id:int}")]
    public async Task<IActionResult> DestroyCustomObject(int id, CancellationToken ct)
    {
        var obj = await _db.CustomObjects.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (obj is null) return NotFound();

        await _db.CustomObjectRecords.Where(r => r.ObjectId == id).ExecuteDeleteAsync(ct);
        _db.CustomObjects.Remove(obj);
        await _db.SaveChangesAsync(ct);

        return new UiRedirectResult($"{Prefix}/admin/custom-objects") { Success = "Custom object deleted." };
    }

    [HttpGet("custom-objects/{id:int}/records")]
    public async Task<IActionResult> CustomObjectRecords(int id, CancellationToken ct)
    {
        var obj = await _db.CustomObjects.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct);
        if (obj is null) return NotFound();

        var records = await _db.CustomObjectRecords.AsNoTracking().Where(r => r.ObjectId == id).OrderByDescending(r => r.Id).ToListAsync(ct);
        return UiResults.Render("Escalated/Admin/CustomObjects/Records", new
        {
            @object = CustomObject(obj),
            records = records.Select(CustomObjectRecord).ToList(),
        });
    }

    [HttpPost("custom-objects/{id:int}/records")]
    public Task<IActionResult> StoreCustomObjectRecord(int id, CancellationToken ct) => SaveCustomObjectRecordAsync(id, null, ct);

    [HttpPut("custom-objects/{id:int}/records/{record:int}")]
    [HttpPatch("custom-objects/{id:int}/records/{record:int}")]
    public Task<IActionResult> UpdateCustomObjectRecord(int id, int record, CancellationToken ct) => SaveCustomObjectRecordAsync(id, record, ct);

    private async Task<IActionResult> SaveCustomObjectRecordAsync(int objectId, int? recordId, CancellationToken ct)
    {
        var obj = await _db.CustomObjects.AsNoTracking().FirstOrDefaultAsync(o => o.Id == objectId, ct);
        if (obj is null) return NotFound();

        var record = recordId is null
            ? new CustomObjectRecord { ObjectId = objectId, CreatedAt = DateTime.UtcNow }
            : await _db.CustomObjectRecords.FirstOrDefaultAsync(r => r.Id == recordId && r.ObjectId == objectId, ct);
        if (record is null) return NotFound();

        var input = await UiInput.ReadAsync(Request, ct);
        var fallback = $"{Prefix}/admin/custom-objects/{objectId}/records";
        var errors = Errors();
        if (input.Json("data") is not { ValueKind: JsonValueKind.Object } data)
        {
            errors["data"] = "The data field is required.";
        }
        else
        {
            ValidateRecordData(obj, data, errors);
            if (errors.Count == 0) record.Data = data.GetRawText();
        }

        if (errors.Count > 0) return UiResults.Back(HttpContext, fallback, errors: errors);

        record.UpdatedAt = DateTime.UtcNow;
        if (recordId is null) _db.CustomObjectRecords.Add(record);
        await _db.SaveChangesAsync(ct);

        return UiResults.Back(HttpContext, fallback, success: recordId is null ? "Record created." : "Record updated.");
    }

    /// <summary>A record's data against the object's schema: required keys, numbers, dates, select options, no unknown keys.</summary>
    private static void ValidateRecordData(CustomObject obj, JsonElement data, Dictionary<string, string> errors)
    {
        var schema = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (UiPresenter.ParseJson(obj.FieldsSchema) is JsonElement { ValueKind: JsonValueKind.Array } fields)
        {
            foreach (var field in fields.EnumerateArray())
            {
                if (field.ValueKind == JsonValueKind.Object && field.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
                    schema[n.GetString()!] = field;
            }
        }

        foreach (var property in data.EnumerateObject())
        {
            if (!schema.ContainsKey(property.Name)) errors[$"data.{property.Name}"] = $"The field \"{property.Name}\" is not defined on this object.";
        }

        foreach (var (name, field) in schema)
        {
            var key = $"data.{name}";
            var required = field.TryGetProperty("required", out var r) && r.ValueKind == JsonValueKind.True;
            var type = field.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : "text";
            var present = data.TryGetProperty(name, out var value);
            var empty = !present || value.ValueKind is JsonValueKind.Null
                || (value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString()));

            if (empty)
            {
                if (required) errors[key] = $"The {name} field is required.";
                continue;
            }

            var text = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null,
            };

            switch (type)
            {
                case "number":
                    if (text is null || !decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                        errors[key] = $"The {name} field must be a number.";
                    break;
                case "date":
                    if (value.ValueKind != JsonValueKind.String
                        || !DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                        errors[key] = $"The {name} field must be a valid date.";
                    break;
                case "select":
                    var options = field.TryGetProperty("options", out var o) && o.ValueKind == JsonValueKind.Array
                        ? o.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : x.GetRawText()).ToList()
                        : new List<string?>();
                    if (text is null || !options.Contains(text)) errors[key] = $"The selected {name} is invalid.";
                    break;
                default:
                    if (text is null) errors[key] = $"The {name} field must be a string.";
                    break;
            }
        }
    }

    [HttpDelete("custom-objects/{id:int}/records/{record:int}")]
    public async Task<IActionResult> DestroyCustomObjectRecord(int id, int record, CancellationToken ct)
    {
        var row = await _db.CustomObjectRecords.FirstOrDefaultAsync(r => r.Id == record && r.ObjectId == id, ct);
        if (row is null) return NotFound();

        _db.CustomObjectRecords.Remove(row);
        await _db.SaveChangesAsync(ct);

        return UiResults.Back(HttpContext, $"{Prefix}/admin/custom-objects/{id}/records", success: "Record deleted.");
    }
}
