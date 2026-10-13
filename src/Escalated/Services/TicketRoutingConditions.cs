using System.Text.Json;
using Escalated.Authorization;
using Escalated.Models;

namespace Escalated.Services;

public static class TicketRoutingConditions
{
    public static List<Dictionary<string, string>> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateArray().Select(condition => condition.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() ?? "" : property.Value.ToString())).ToList();
    }

    public static TicketAccount? Account(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var value = document.RootElement;
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("type", out var type) || !value.TryGetProperty("id", out var id)) return null;
            if (type.ValueKind != JsonValueKind.String || id.ValueKind is not (JsonValueKind.String or JsonValueKind.Number)) return null;
            var typeValue = type.GetString();
            var idValue = id.ToString();
            return string.IsNullOrEmpty(typeValue) || string.IsNullOrEmpty(idValue) ? null : new TicketAccount(typeValue, idValue);
        }
        catch (JsonException) { return null; }
    }

    public static IQueryable<Ticket> WhereAccount(IQueryable<Ticket> query, string value, string op = "equals")
    {
        var account = Account(value);
        if (account is null) return query.Where(_ => false);
        return op switch
        {
            "equals" or "=" => query.Where(t => t.AccountType == account.Type && t.AccountId == account.Id),
            "not_equals" or "!=" => query.Where(t => t.AccountType == null || t.AccountId == null || t.AccountType != account.Type || t.AccountId != account.Id),
            _ => query.Where(_ => false),
        };
    }
}
