using Escalated.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace Escalated.Authorization;

/// <summary>
/// Thrown when a request reached a staff route (one behind the <c>escalated-admin</c>,
/// <c>escalated-agent</c> or <c>escalated-user</c> policy) but Escalated cannot tell which
/// host user it acts as. Escalated's own policies refuse such a request; a host policy that
/// replaced them (for example <c>RequireRole("SupportAgent")</c>) can let it through, and
/// every staff action would then run as nobody. The message says how to fix it.
/// </summary>
public sealed class EscalatedUserNotResolvedException : InvalidOperationException
{
    public EscalatedUserNotResolvedException(string policy)
        : base(
            $"Escalated could not resolve the signed-in user for a staff route (policy '{policy}'). " +
            "The request passed that policy, but Escalated reads the host user id from the principal's " +
            "NameIdentifier claim, then 'sub', then 'id', and found none. Sign users in with their id in one of " +
            "those claims, or tell Escalated where it is: builder.Services.Configure<EscalatedOptions>(o => " +
            "o.UserIdResolver = user => user.FindFirst(\"your_id_claim\")?.Value). " +
            "See \"Who a request acts as\" in docs/getting-started.md.")
    {
        Policy = policy;
    }

    /// <summary>The staff policy the request passed.</summary>
    public string Policy { get; }
}

/// <summary>
/// Stops a staff request whose user Escalated cannot identify with
/// <see cref="EscalatedUserNotResolvedException"/>, before any action runs as nobody.
/// Registered globally; it looks only at endpoints behind a staff policy, and skips
/// <c>[AllowAnonymous]</c> ones (plugin webhooks).
/// </summary>
public sealed class StaffUserResolutionFilter : IResourceFilter
{
    private static readonly HashSet<string> StaffPolicies = new(StringComparer.Ordinal)
    {
        EscalatedPolicies.Admin, EscalatedPolicies.Agent, EscalatedPolicies.User,
    };

    private readonly IOptions<EscalatedOptions> _options;

    public StaffUserResolutionFilter(IOptions<EscalatedOptions> options)
    {
        _options = options;
    }

    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var metadata = context.ActionDescriptor.EndpointMetadata;
        if (metadata.OfType<IAllowAnonymous>().Any())
        {
            return;
        }

        var policy = metadata.OfType<IAuthorizeData>()
            .Select(d => d.Policy)
            .FirstOrDefault(p => p is not null && StaffPolicies.Contains(p));
        if (policy is null)
        {
            return;
        }

        if (EscalatedUser.ResolveId(context.HttpContext.User, _options.Value) is null)
        {
            throw new EscalatedUserNotResolvedException(policy);
        }
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
