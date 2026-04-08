using Escalated.Data;
using Escalated.Models;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Services;

/// <summary>
/// Evaluates chat routing rules to determine which agent or department
/// should receive an incoming chat session.
/// </summary>
public class ChatRoutingService
{
    private readonly EscalatedDbContext _db;

    public ChatRoutingService(EscalatedDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Resolve the best routing target for a new chat session.
    /// Evaluates active rules ordered by priority. Falls back to the
    /// requested department if no rules match.
    /// </summary>
    public async Task<ChatRouteResult> ResolveAsync(int? requestedDepartmentId = null, CancellationToken ct = default)
    {
        var rules = await _db.ChatRoutingRules
            .Where(r => r.IsActive)
            .OrderBy(r => r.Priority)
            .ToListAsync(ct);

        foreach (var rule in rules)
        {
            if (rule.DepartmentId.HasValue && rule.DepartmentId == requestedDepartmentId)
            {
                return new ChatRouteResult(rule.DepartmentId, rule.AgentId);
            }

            // If no specific department requested, use first matching rule
            if (!requestedDepartmentId.HasValue)
            {
                return new ChatRouteResult(rule.DepartmentId, rule.AgentId);
            }
        }

        return new ChatRouteResult(requestedDepartmentId, null);
    }

    /// <summary>
    /// CRUD operations for routing rules.
    /// </summary>
    public async Task<ChatRoutingRule> CreateRuleAsync(
        string name,
        int? departmentId = null,
        int? agentId = null,
        string? conditions = null,
        int priority = 0,
        CancellationToken ct = default)
    {
        var rule = new ChatRoutingRule
        {
            Name = name,
            DepartmentId = departmentId,
            AgentId = agentId,
            Conditions = conditions,
            Priority = priority,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.ChatRoutingRules.Add(rule);
        await _db.SaveChangesAsync(ct);
        return rule;
    }

    public async Task<ChatRoutingRule> UpdateRuleAsync(
        int ruleId,
        string? name = null,
        int? departmentId = null,
        int? agentId = null,
        string? conditions = null,
        int? priority = null,
        bool? isActive = null,
        CancellationToken ct = default)
    {
        var rule = await _db.ChatRoutingRules.FindAsync(new object[] { ruleId }, ct)
            ?? throw new InvalidOperationException("Routing rule not found.");

        if (name != null) rule.Name = name;
        if (departmentId.HasValue) rule.DepartmentId = departmentId;
        if (agentId.HasValue) rule.AgentId = agentId;
        if (conditions != null) rule.Conditions = conditions;
        if (priority.HasValue) rule.Priority = priority.Value;
        if (isActive.HasValue) rule.IsActive = isActive.Value;
        rule.UpdatedAt = DateTime.UtcNow;

        _db.ChatRoutingRules.Update(rule);
        await _db.SaveChangesAsync(ct);
        return rule;
    }

    public async Task DeleteRuleAsync(int ruleId, CancellationToken ct = default)
    {
        var rule = await _db.ChatRoutingRules.FindAsync(new object[] { ruleId }, ct);
        if (rule != null)
        {
            _db.ChatRoutingRules.Remove(rule);
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task<List<ChatRoutingRule>> GetAllRulesAsync(CancellationToken ct = default)
    {
        return await _db.ChatRoutingRules
            .OrderBy(r => r.Priority)
            .ToListAsync(ct);
    }
}

public record ChatRouteResult(int? DepartmentId, int? AgentId);
