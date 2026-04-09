using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Escalated.Services;

public class WorkflowEngine
{
    public static readonly string[] Operators = { "equals", "not_equals", "contains", "not_contains", "starts_with", "ends_with", "greater_than", "less_than", "greater_or_equal", "less_or_equal", "is_empty", "is_not_empty" };
    public static readonly string[] ActionTypes = { "change_status", "assign_agent", "change_priority", "add_tag", "remove_tag", "set_department", "add_note", "send_webhook", "set_type", "delay", "add_follower", "send_notification" };
    public static readonly string[] TriggerEvents = { "ticket.created", "ticket.updated", "ticket.status_changed", "ticket.assigned", "ticket.priority_changed", "ticket.tagged", "ticket.department_changed", "reply.created", "reply.agent_reply", "sla.warning", "sla.breached", "ticket.reopened" };

    public bool EvaluateConditions(Dictionary<string, object> conditions, Dictionary<string, string> ticket)
    {
        if (conditions.TryGetValue("all", out var allObj) && allObj is List<Dictionary<string, string>> allConds)
            return allConds.All(c => EvaluateSingle(c, ticket));
        if (conditions.TryGetValue("any", out var anyObj) && anyObj is List<Dictionary<string, string>> anyConds)
            return anyConds.Any(c => EvaluateSingle(c, ticket));
        return true;
    }

    public bool EvaluateSingle(Dictionary<string, string> condition, Dictionary<string, string> ticket)
    {
        var field = condition.GetValueOrDefault("field", "");
        var op = condition.GetValueOrDefault("operator", "equals");
        var expected = condition.GetValueOrDefault("value", "");
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
