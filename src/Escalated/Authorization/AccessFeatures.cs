using Escalated.Configuration;
using Microsoft.Extensions.Options;

namespace Escalated.Authorization;

/// <summary>
/// <c>Escalated:Access</c>: the install-level access switches (ADR
/// 2026-10-29-configurable-access-features). Each one left unset falls back to its
/// environment variable (<c>ESCALATED_ROLES_PERMISSIONS</c>, <c>ESCALATED_TWO_FACTOR</c>,
/// <c>ESCALATED_SSO</c>), else off. Never read from the settings table.
/// </summary>
public class AccessOptions
{
    public bool? RolesPermissions { get; set; }
    public bool? TwoFactor { get; set; }
    public bool? Sso { get; set; }
}

/// <summary>
/// Which access features are on. A feature this port does not enforce yet is reported
/// off whatever its switch says, so no screen claims a protection that is not there.
/// </summary>
public sealed class AccessFeatures
{
    public const string RolesPermissionsKey = "roles_permissions";
    public const string TwoFactorKey = "two_factor";
    public const string SsoKey = "sso";

    /// <summary>The features this port enforces.</summary>
    internal static readonly HashSet<string> Enforced = new() { RolesPermissionsKey };

    private readonly IOptions<EscalatedOptions> _options;

    public AccessFeatures(IOptions<EscalatedOptions> options)
    {
        _options = options;
    }

    public bool RolesPermissions => Enabled(RolesPermissionsKey);
    public bool TwoFactor => Enabled(TwoFactorKey);
    public bool Sso => Enabled(SsoKey);

    public bool Enabled(string feature)
    {
        if (!Enforced.Contains(feature))
        {
            return false;
        }

        var access = _options.Value.Access;
        var configured = feature switch
        {
            RolesPermissionsKey => access.RolesPermissions,
            TwoFactorKey => access.TwoFactor,
            SsoKey => access.Sso,
            _ => null,
        };

        return configured ?? Truthy(Environment.GetEnvironmentVariable("ESCALATED_" + feature.ToUpperInvariant()));
    }

    /// <summary>The <c>escalated.features</c> entries the shared frontend reads.</summary>
    public Dictionary<string, object?> Shared() => new()
    {
        [RolesPermissionsKey] = RolesPermissions,
        [TwoFactorKey] = TwoFactor,
        [SsoKey] = Sso,
    };

    internal static bool Truthy(string? value) =>
        value is not null && value.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on";
}
