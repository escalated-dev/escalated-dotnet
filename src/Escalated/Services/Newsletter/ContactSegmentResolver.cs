using System.Text.Json;
using Escalated.Data;
using Escalated.Models;
using Escalated.Models.Newsletter;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Services.Newsletter;

public class ContactSegmentResolver
{
    private static readonly HashSet<string> AllowedFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "id",
        "email",
        "name",
        "user_id",
        "created_at",
        "updated_at",
        "marketing_opt_out_at",
    };

    private static readonly HashSet<string> AllowedOps = new(StringComparer.OrdinalIgnoreCase)
    {
        "=",
        "!=",
        ">",
        ">=",
        "<",
        "<=",
        "contains",
        "starts_with",
        "ends_with",
        "in",
        "is_null",
        "not_null",
    };

    private readonly EscalatedDbContext _db;

    public ContactSegmentResolver(EscalatedDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<int>> ResolveAsync(NewsletterList list, CancellationToken ct = default)
    {
        if (list.Kind == "static")
        {
            return await _db.NewsletterListMembers
                .Where(m => m.ListId == list.Id)
                .Select(m => m.ContactId)
                .ToListAsync(ct);
        }

        return (await ApplyFilterAsync(list.FilterJson, includeOptedOut: true, ct))
            .Select(c => c.Id)
            .ToList();
    }

    public async Task<IReadOnlyList<int>> ResolveSendableAsync(NewsletterList list, CancellationToken ct = default)
    {
        if (list.Kind == "static")
        {
            var ids = await _db.NewsletterListMembers
                .Where(m => m.ListId == list.Id)
                .Select(m => m.ContactId)
                .ToListAsync(ct);

            if (ids.Count == 0)
                return Array.Empty<int>();

            return await _db.Contacts
                .Where(c => ids.Contains(c.Id) && c.MarketingOptOutAt == null)
                .Select(c => c.Id)
                .ToListAsync(ct);
        }

        return (await ApplyFilterAsync(list.FilterJson, includeOptedOut: false, ct))
            .Select(c => c.Id)
            .ToList();
    }

    public async Task<int> CountMatchesAsync(string? filterJson, CancellationToken ct = default) =>
        (await ApplyFilterAsync(filterJson, includeOptedOut: true, ct)).Count;

    private async Task<List<Contact>> ApplyFilterAsync(string? filterJson, bool includeOptedOut, CancellationToken ct)
    {
        var contacts = await _db.Contacts
            .Where(c => includeOptedOut || c.MarketingOptOutAt == null)
            .ToListAsync(ct);

        foreach (var rule in ParseRules(filterJson))
        {
            contacts = contacts.Where(c => Matches(c, rule)).ToList();
        }

        return contacts;
    }

    private static IEnumerable<SegmentRule> ParseRules(string? filterJson)
    {
        if (string.IsNullOrWhiteSpace(filterJson))
            yield break;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(filterJson);
        }
        catch (JsonException)
        {
            yield break;
        }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("rules", out var rules) || rules.ValueKind != JsonValueKind.Array)
                yield break;

            foreach (var item in rules.EnumerateArray())
            {
                var field = item.TryGetProperty("field", out var fieldEl) ? fieldEl.GetString() : null;
                var op = item.TryGetProperty("op", out var opEl) ? opEl.GetString() : "=";
                if (string.IsNullOrWhiteSpace(field) || string.IsNullOrWhiteSpace(op))
                    continue;

                if (!IsAllowedField(field) || !AllowedOps.Contains(op))
                    continue;

                var value = item.TryGetProperty("value", out var valueEl)
                    ? JsonElementToString(valueEl)
                    : null;
                yield return new SegmentRule(field, op, value);
            }
        }
    }

    private static bool IsAllowedField(string field) =>
        AllowedFields.Contains(field) || field.StartsWith("metadata.", StringComparison.OrdinalIgnoreCase);

    private static bool Matches(Contact contact, SegmentRule rule)
    {
        var actual = ResolveField(contact, rule.Field);

        return rule.Op switch
        {
            "is_null" => actual is null,
            "not_null" => actual is not null,
            "=" => Compare(actual, rule.Value) == 0,
            "!=" => Compare(actual, rule.Value) != 0,
            ">" => Compare(actual, rule.Value) > 0,
            ">=" => Compare(actual, rule.Value) >= 0,
            "<" => Compare(actual, rule.Value) < 0,
            "<=" => Compare(actual, rule.Value) <= 0,
            "contains" => (actual ?? "").Contains(rule.Value ?? "", StringComparison.OrdinalIgnoreCase),
            "starts_with" => (actual ?? "").StartsWith(rule.Value ?? "", StringComparison.OrdinalIgnoreCase),
            "ends_with" => (actual ?? "").EndsWith(rule.Value ?? "", StringComparison.OrdinalIgnoreCase),
            "in" => (rule.Value ?? "")
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Any(v => string.Equals(v, actual, StringComparison.OrdinalIgnoreCase)),
            _ => false,
        };
    }

    private static string? ResolveField(Contact contact, string field)
    {
        if (field.StartsWith("metadata.", StringComparison.OrdinalIgnoreCase))
        {
            var key = field["metadata.".Length..];
            var metadata = contact.GetMetadata();
            return metadata.TryGetValue(key, out var value) ? value?.ToString() : null;
        }

        return field.ToLowerInvariant() switch
        {
            "id" => contact.Id.ToString(),
            "email" => contact.Email,
            "name" => contact.Name,
            "user_id" => contact.UserId,
            "created_at" => contact.CreatedAt.ToString("O"),
            "updated_at" => contact.UpdatedAt.ToString("O"),
            "marketing_opt_out_at" => contact.MarketingOptOutAt?.ToString("O"),
            _ => null,
        };
    }

    private static int Compare(string? actual, string? expected)
    {
        if (DateTime.TryParse(actual, out var actualDate) && DateTime.TryParse(expected, out var expectedDate))
            return actualDate.CompareTo(expectedDate);

        if (decimal.TryParse(actual, out var actualNumber) && decimal.TryParse(expected, out var expectedNumber))
            return actualNumber.CompareTo(expectedNumber);

        return string.Compare(actual ?? "", expected ?? "", StringComparison.OrdinalIgnoreCase);
    }

    private static string? JsonElementToString(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Array => string.Join(",", value.EnumerateArray().Select(JsonElementToString)),
            _ => value.ToString(),
        };

    private sealed record SegmentRule(string Field, string Op, string? Value);
}
