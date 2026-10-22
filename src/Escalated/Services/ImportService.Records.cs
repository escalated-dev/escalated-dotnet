using System.Text.Json;
using System.Text.RegularExpressions;
using Escalated.Models;
using Escalated.Services.Email.Inbound;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Services;

/// <summary>
/// The custom field, attachment and satisfaction rating persisters, as
/// escalated-laravel's <c>ImportService</c> has them, held to the rules the live
/// paths apply: custom field values are checked like a ticket update's, attachment
/// bytes like a page upload's.
/// </summary>
public partial class ImportService
{
    /// <summary>Problems a record was imported past, logged against it by the run.</summary>
    private readonly List<(string EntityType, string Message)> _recordErrors = new();

    private static JsonElement? Json(object? value) => value switch
    {
        null => null,
        JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => null,
        JsonElement e => e,
        _ => JsonSerializer.SerializeToElement(value),
    };

    /// <summary>
    /// Laravel's <c>persistCustomField</c>: the definition is found by its slug or
    /// created with the source's name, type and options. Unlike Laravel, the type
    /// must be one the custom field admin accepts, and a select needs options.
    /// </summary>
    private async Task<string> PersistCustomFieldAsync(Dictionary<string, object?> record, CancellationToken ct)
    {
        var name = Text(record, "name") ?? throw new InvalidOperationException("Custom field has no name.");
        var slug = Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');
        if (slug.Length == 0) slug = "field";
        if (slug.Length > 90) slug = slug[..90].TrimEnd('_');

        var existing = await _db.CustomFields.AsNoTracking().Where(f => f.Slug == slug).Select(f => (int?)f.Id).FirstOrDefaultAsync(ct);
        if (existing is { } id) return id.ToString();

        var type = Text(record, "type") ?? "text";
        if (!CustomFieldService.Types.Contains(type)) throw new InvalidOperationException($"Unknown custom field type '{type}'.");
        var options = Json(record.GetValueOrDefault("options")) is { ValueKind: JsonValueKind.Array } list
            ? list.EnumerateArray().Where(o => o.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(o.GetString()))
                .Select(o => o.GetString()!.Trim()).ToList()
            : new List<string>();
        if (type is "select" or "multi_select" && options.Count == 0)
            throw new InvalidOperationException($"A {type} custom field needs at least one option.");

        var field = new CustomField
        {
            Name = name,
            Slug = slug,
            Type = type,
            Context = CustomFieldService.TicketEntity,
            Options = options.Count > 0 ? JsonSerializer.Serialize(options) : null,
            Active = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        _db.CustomFields.Add(field);
        await _db.SaveChangesAsync(ct);
        return field.Id.ToString();
    }

    /// <summary>
    /// The custom field a ticket record's <c>custom_fields</c> key names. The wizard's
    /// ticket field mapping decides first: a key mapped to <c>custom_fields.&lt;slug&gt;</c>
    /// lands on that field, a key mapped to "" (Skip field) is dropped. Otherwise the
    /// key is a custom field's source id from this import, or a field slug.
    /// </summary>
    private async Task<(string? Slug, string? Problem)> FieldSlugForAsync(ImportJob job, JsonElement? mappings, string key, CancellationToken ct)
    {
        if (mappings is { ValueKind: JsonValueKind.Object } m && m.TryGetProperty(key, out var target))
        {
            var t = target.ValueKind == JsonValueKind.String ? target.GetString()!.Trim() : "";
            if (t.Length == 0) return (null, null);
            if (t.StartsWith("custom_fields.", StringComparison.Ordinal)) return (t["custom_fields.".Length..], null);
            return (null, $"custom_fields.{key}: mapped to '{t}', which is not a custom field.");
        }

        if (await ResolveAsync(job.Id, "custom_fields", key, ct) is { } fieldId && int.TryParse(fieldId, out var id))
        {
            var slug = await _db.CustomFields.AsNoTracking().Where(f => f.Id == id).Select(f => f.Slug).FirstOrDefaultAsync(ct);
            if (slug is not null) return (slug, null);
        }

        return (key, null);
    }

    /// <summary>
    /// Stores a ticket record's <c>custom_fields</c> values, each checked as a ticket
    /// update checks it (active ticket fields, type, options). One that fails is left
    /// out and logged under <c>custom_field_values</c>; the ticket is kept. Fields the
    /// record does not carry are not required: imported history predates them.
    /// </summary>
    private async Task ImportFieldValuesAsync(ImportJob job, int ticketId, Dictionary<string, object?> record, CancellationToken ct)
    {
        if (Json(record.GetValueOrDefault("custom_fields")) is not { ValueKind: JsonValueKind.Object } values) return;

        JsonElement? mappings = null;
        if (!string.IsNullOrEmpty(job.FieldMappings))
        {
            try
            {
                using var doc = JsonDocument.Parse(job.FieldMappings);
                if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("tickets", out var t)) mappings = t.Clone();
            }
            catch (JsonException)
            {
            }
        }

        var fields = new CustomFieldService(_db);
        var plan = new CustomFieldPlan();
        foreach (var property in values.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            var (slug, problem) = await FieldSlugForAsync(job, mappings, property.Name, ct);
            if (problem is not null)
            {
                _recordErrors.Add(("custom_field_values", problem));
                continue;
            }
            if (slug is null) continue;

            var input = JsonSerializer.SerializeToElement(new Dictionary<string, JsonElement> { [slug] = property.Value });
            var checkedValue = await fields.ValidateTicketAsync(input, creating: false, ct);
            if (!checkedValue.IsValid)
            {
                foreach (var (key, message) in checkedValue.Errors) _recordErrors.Add(("custom_field_values", $"{key}: {message}"));
                continue;
            }
            foreach (var (fieldId, value) in checkedValue.Values) plan.Values[fieldId] = value;
        }

        await fields.ApplyTicketAsync(ticketId, plan, ct);
    }

    /// <summary>A record's file bytes: a byte array or a base64 string.</summary>
    private static byte[]? Content(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case byte[] bytes:
                return bytes;
            case JsonElement { ValueKind: JsonValueKind.Null }:
                return null;
        }

        var text = value is JsonElement { ValueKind: JsonValueKind.String } e ? e.GetString() : value as string;
        if (text is null) throw new InvalidOperationException("Attachment content must be bytes or a base64 string.");
        if (text.Trim().Length == 0) return null;
        try
        {
            return Convert.FromBase64String(text.Trim());
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Attachment content is not valid base64.");
        }
    }

    /// <summary>
    /// Laravel's <c>persistAttachment</c> (<c>parent_type</c> ticket or reply, default
    /// reply; <c>parent_source_id</c>), with a page upload's rules: the record carries
    /// the file's bytes (<c>content</c>), which are checked against
    /// <see cref="Configuration.AttachmentOptions.MaxSizeKb"/> and the blocked file
    /// types and written through <see cref="IAttachmentStorage"/>. A storage path
    /// from the source is never trusted.
    /// </summary>
    private async Task<string> PersistAttachmentAsync(ImportJob job, Dictionary<string, object?> record, CancellationToken ct)
    {
        var parentType = Text(record, "parent_type") ?? "reply";
        if (parentType is not ("ticket" or "reply")) throw new InvalidOperationException($"Unknown attachment parent type '{parentType}'.");
        var parentId = await RefAsync(job, parentType == "ticket" ? "tickets" : "replies", record.GetValueOrDefault("parent_source_id"), ct)
            ?? throw new InvalidOperationException($"Parent {parentType} not found for attachment.");

        if (_storage is null) throw new InvalidOperationException("No attachment storage is registered (IAttachmentStorage).");
        var content = Content(record.GetValueOrDefault("content"));
        if (content is null || content.Length == 0)
            throw new InvalidOperationException("Attachment has no content: imports store the file's bytes, never a source path.");

        var name = AttachmentDownloader.SafeFilename(Text(record, "filename"));
        if (name.Length > 255) name = name[..255];
        if (InboundEmailService.BlockedExtensions.Contains(Path.GetExtension(name).TrimStart('.')))
            throw new InvalidOperationException($"{name} is a file type that cannot be attached.");
        if (_attachments.MaxSizeKb > 0 && content.LongLength > (long)_attachments.MaxSizeKb * 1024)
            throw new InvalidOperationException($"{name} is larger than {_attachments.MaxSizeKb} KB.");

        var contentType = Text(record, "mime_type") ?? "application/octet-stream";
        if (contentType.Length > 100) contentType = "application/octet-stream";

        using var stream = new MemoryStream(content);
        var path = await _storage.PutAsync(name, stream, contentType, ct);
        var attachment = new Attachment
        {
            AttachableType = parentType,
            AttachableId = int.Parse(parentId),
            Filename = name,
            MimeType = contentType,
            Size = content.LongLength,
            Path = path,
            Disk = _storage.Name,
            CreatedAt = Timestamp(record, "created_at") ?? DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        _db.Attachments.Add(attachment);
        await _db.SaveChangesAsync(ct);
        return attachment.Id.ToString();
    }

    private static int? Rating(object? value)
    {
        var raw = value switch
        {
            null => null,
            JsonElement { ValueKind: JsonValueKind.Number or JsonValueKind.String } e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.GetRawText(),
            JsonElement => null,
            IConvertible c => Convert.ToString(c, System.Globalization.CultureInfo.InvariantCulture),
            _ => null,
        };
        if (raw is null || !decimal.TryParse(raw.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n)) return null;
        if (n != decimal.Truncate(n) || n < 1 || n > 5) return null;
        return (int)n;
    }

    /// <summary>
    /// Laravel's <c>persistSatisfactionRating</c>: the rating (or score) and comment,
    /// on the imported ticket, at the source's time. Only the row is written: no
    /// rater, no resolved/closed rule, nothing a live rating sets off. A ticket keeps
    /// one rating and a comment the live 2,000-character limit, as live ratings do.
    /// </summary>
    private async Task<string> PersistSatisfactionRatingAsync(ImportJob job, Dictionary<string, object?> record, CancellationToken ct)
    {
        var ticketId = await RefAsync(job, "tickets", record.GetValueOrDefault("ticket_source_id"), ct)
            ?? throw new InvalidOperationException("Ticket not found for satisfaction rating.");
        var id = int.Parse(ticketId);
        var raw = record.GetValueOrDefault("rating");
        if (raw is null or JsonElement { ValueKind: JsonValueKind.Null }) raw = record.GetValueOrDefault("score");
        var rating = Rating(raw) ?? throw new InvalidOperationException("The rating must be an integer between 1 and 5.");
        var comment = Text(record, "comment");
        if (comment is { Length: > 2000 }) throw new InvalidOperationException("The comment may not be greater than 2000 characters.");
        if (await _db.SatisfactionRatings.AnyAsync(r => r.TicketId == id, ct))
            throw new InvalidOperationException("The ticket already has a satisfaction rating.");

        var row = new SatisfactionRating
        {
            TicketId = id,
            Rating = rating,
            Comment = comment,
            CreatedAt = Timestamp(record, "created_at") ?? DateTime.UtcNow,
        };
        _db.SatisfactionRatings.Add(row);
        await _db.SaveChangesAsync(ct);
        return row.Id.ToString();
    }
}
