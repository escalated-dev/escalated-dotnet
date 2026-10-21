using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Escalated.Services;

public class WorkflowEngine
{
    public static readonly string[] Operators = { "equals", "not_equals", "contains", "not_contains", "starts_with", "ends_with", "greater_than", "less_than", "greater_or_equal", "less_or_equal", "is_empty", "is_not_empty" };
    public static readonly string[] ActionTypes = { "change_status", "assign_agent", "change_priority", "add_tag", "remove_tag", "set_department", "add_note", "send_webhook", "set_type", "delay", "add_follower", "send_notification" };
    /// <summary>
    /// The triggers a Workflow can be saved with: the shared event vocabulary
    /// (ADR 2026-10-21) minus <c>chat.transferred</c>, which this package does not
    /// raise yet. Legacy names are still read from saved Workflows, never offered.
    /// </summary>
    public static readonly string[] TriggerEvents = Escalated.Events.EventNames.Canonical
        .Where(name => name != Escalated.Events.EventNames.ChatTransferred).ToArray();

    public bool EvaluateConditions(Dictionary<string, object> conditions, Dictionary<string, string> ticket)
    {
        if (conditions.TryGetValue("all", out var allObj))
        {
            var allConds = NormalizeConditionList(allObj);
            if (allConds is not null) return allConds.All(c => EvaluateSingle(c, ticket));
        }
        if (conditions.TryGetValue("any", out var anyObj))
        {
            var anyConds = NormalizeConditionList(anyObj);
            if (anyConds is not null) return anyConds.Any(c => EvaluateSingle(c, ticket));
        }
        return true;
    }

    /// <summary>
    /// Conditions arrive as either a `List&lt;Dictionary&lt;string,string&gt;&gt;`
    /// (constructed in code, e.g. DryRun callers) or a `JsonElement`
    /// (deserialized from `Workflow.Conditions`). Normalize both shapes
    /// to a list the rest of the engine can evaluate. Returns null when
    /// the value isn't a recognizable condition list — caller treats
    /// that as "no constraint".
    /// </summary>
    private static List<Dictionary<string, string>>? NormalizeConditionList(object value)
    {
        if (value is List<Dictionary<string, string>> list)
        {
            return list;
        }
        if (value is JsonElement element && element.ValueKind == JsonValueKind.Array)
        {
            var result = new List<Dictionary<string, string>>();
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var dict = new Dictionary<string, string>();
                foreach (var prop in item.EnumerateObject())
                {
                    dict[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                        ? prop.Value.GetString() ?? ""
                        : prop.Value.ToString();
                }
                result.Add(dict);
            }
            return result;
        }
        return null;
    }

    public bool EvaluateSingle(Dictionary<string, string> condition, Dictionary<string, string> ticket)
    {
        var field = condition.GetValueOrDefault("field", "");
        var op = condition.GetValueOrDefault("operator", "equals");
        var expected = condition.GetValueOrDefault("value", "");
        if (field == "account")
        {
            var type = ticket.GetValueOrDefault("account_type", "");
            var id = ticket.GetValueOrDefault("account_id", "");
            var empty = string.IsNullOrEmpty(type) || string.IsNullOrEmpty(id);
            if (op == "is_empty") return empty;
            if (op == "is_not_empty") return !empty;
            var account = TicketRoutingConditions.Account(expected);
            if (account is null) return false;
            var equal = !empty && type == account.Type && id == account.Id;
            return op == "equals" ? equal : op == "not_equals" && !equal;
        }
        var actual = ticket.GetValueOrDefault(field, "");
        return ApplyOperator(op, actual, expected);
    }

    public static bool ApplyOperator(string op, string actual, string expected)
    {
        return op switch
        {
            "equals" => actual == expected,
            "not_equals" => actual != expected,
            "contains" => actual.Contains(expected),
            "not_contains" => !actual.Contains(expected),
            "starts_with" => actual.StartsWith(expected),
            "ends_with" => actual.EndsWith(expected),
            "greater_than" => double.TryParse(actual, out var a1) && double.TryParse(expected, out var e1) && a1 > e1,
            "less_than" => double.TryParse(actual, out var a2) && double.TryParse(expected, out var e2) && a2 < e2,
            "greater_or_equal" => double.TryParse(actual, out var a3) && double.TryParse(expected, out var e3) && a3 >= e3,
            "less_or_equal" => double.TryParse(actual, out var a4) && double.TryParse(expected, out var e4) && a4 <= e4,
            "is_empty" => string.IsNullOrWhiteSpace(actual),
            "is_not_empty" => !string.IsNullOrWhiteSpace(actual),
            _ => false,
        };
    }

    public static string InterpolateVariables(string text, Dictionary<string, string> ticket)
    {
        return Regex.Replace(text, @"\{\{(\w+)\}\}", match =>
        {
            var varName = match.Groups[1].Value;
            return ticket.GetValueOrDefault(varName, match.Value);
        });
    }

    public object DryRun(Dictionary<string, object> conditions, List<Dictionary<string, string>> actions, Dictionary<string, string> ticket)
    {
        var matched = EvaluateConditions(conditions, ticket);
        var previews = actions.Select(a => new
        {
            type = a.GetValueOrDefault("type", ""),
            value = InterpolateVariables(a.GetValueOrDefault("value", ""), ticket),
            would_execute = matched,
        }).ToList();
        return new { matched, actions = previews };
    }
}
