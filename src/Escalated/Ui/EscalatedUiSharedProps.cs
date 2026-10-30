using System.Security.Claims;
using Escalated.Authorization;
using Escalated.Configuration;
using Escalated.Controllers.Widget;
using Escalated.Data;
using Escalated.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Escalated.Ui;

/// <summary>
/// Adds props to every page, as Laravel's <c>Inertia::share</c> does. Every
/// registered implementation runs, in registration order, before the page's own
/// props are merged on top. Register more to share your own data.
/// </summary>
public interface IEscalatedUiSharedProps
{
    Task ShareAsync(HttpContext context, IDictionary<string, object?> props, CancellationToken ct = default);
}

/// <summary>
/// What escalated-laravel shares (<c>EscalatedServiceProvider::shareInertiaData</c>
/// plus the host's <c>HandleInertiaRequests</c>): <c>auth.user</c>, <c>flash</c>,
/// <c>errors</c> and the <c>escalated</c> object the layout reads (<c>prefix</c>,
/// <c>is_agent</c>, <c>is_admin</c>, <c>permissions</c>, <c>features</c>, the
/// knowledge base and guest ticket switches, <c>agent_type</c>).
/// </summary>
public sealed class EscalatedUiSharedProps : IEscalatedUiSharedProps
{
    private readonly EscalatedDbContext _db;
    private readonly SettingsService _settings;
    private readonly IAuthorizationService _authorization;
    private readonly IUserDirectory _users;
    private readonly EscalatedOptions _options;
    private readonly AccessFeatures _features;
    private readonly AccessControl _access;
    private readonly TwoFactorService _twoFactor;

    public EscalatedUiSharedProps(EscalatedDbContext db, SettingsService settings, IAuthorizationService authorization,
        IUserDirectory users, IOptions<EscalatedOptions> options, AccessFeatures features, AccessControl access,
        TwoFactorService twoFactor)
    {
        _twoFactor = twoFactor;
        _features = features;
        _access = access;
        _db = db;
        _settings = settings;
        _authorization = authorization;
        _users = users;
        _options = options.Value;
    }

    public async Task ShareAsync(HttpContext context, IDictionary<string, object?> props, CancellationToken ct = default)
    {
        var principal = context.User;
        var userId = principal.Identity?.IsAuthenticated == true ? EscalatedUser.ResolveId(principal, _options) : null;

        var isAdmin = userId is not null && (await _authorization.AuthorizeAsync(principal, EscalatedPolicies.Admin)).Succeeded;
        var isAgent = isAdmin || (userId is not null && (await _authorization.AuthorizeAsync(principal, EscalatedPolicies.Agent)).Succeeded);

        props["auth"] = new Dictionary<string, object?>
        {
            ["user"] = userId is null ? null : await UserAsync(principal, userId, ct),
        };

        var escalated = new Dictionary<string, object?>
        {
            ["prefix"] = _options.RoutePrefix,
            ["broadcasting"] = new Dictionary<string, object?> { ["channel_prefix"] = "escalated" },
            ["is_agent"] = isAgent,
            ["is_admin"] = isAdmin,
            // ADR 2026-10-29-configurable-access-features: the user's slugs while roles and
            // permissions is on; ['*'] for staff while it is off (the gates decide everything).
            ["permissions"] = userId is null ? new List<string>() : await _access.SharedAsync(userId, isAdmin || isAgent, ct),
            ["features"] = Features(),
            ["two_factor_required"] = (isAdmin || isAgent) && await _twoFactor.RequiredAsync(ct),
            ["guest_tickets_enabled"] = await GuestSettings.IsOnAsync(_settings, GuestSettings.GuestTicketsEnabled, ct),
            ["show_powered_by"] = await GuestSettings.IsOnAsync(_settings, "show_powered_by", ct),
            ["knowledge_base_enabled"] = _options.KnowledgeBase.Enabled,
            ["knowledge_base_public"] = await GuestSettings.IsOnAsync(_settings, "knowledge_base_public", ct),
            ["knowledge_base_feedback_enabled"] = _options.KnowledgeBase.AllowFeedback,
        };

        if (userId is not null)
        {
            var agentType = await _db.AgentProfiles.AsNoTracking()
                .Where(p => p.UserId == userId).Select(p => p.AgentType).FirstOrDefaultAsync(ct);
            escalated["agent_type"] = agentType ?? "full";
        }

        props["escalated"] = escalated;
    }

    private async Task<Dictionary<string, object?>> UserAsync(ClaimsPrincipal principal, string userId, CancellationToken ct)
    {
        var entry = await _users.FindAsync(userId, ct);
        return new Dictionary<string, object?>
        {
            ["id"] = userId,
            ["name"] = entry?.Name ?? principal.FindFirstValue(ClaimTypes.Name) ?? principal.FindFirstValue("name") ?? principal.Identity?.Name,
            ["email"] = entry?.Email ?? principal.FindFirstValue(ClaimTypes.Email) ?? principal.FindFirstValue("email"),
        };
    }

    private Dictionary<string, object?> Features()
    {
        var features = new Dictionary<string, object?> { ["newsletters"] = _options.EnableNewsletters };
        foreach (var (key, value) in _features.Shared())
        {
            features[key] = value;
        }

        return features;
    }
}
