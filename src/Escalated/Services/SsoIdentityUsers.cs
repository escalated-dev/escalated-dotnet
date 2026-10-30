using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Escalated.Services;

/// <summary>
/// The SSO defaults for a host on ASP.NET Core Identity. When the host registered
/// <c>UserManager&lt;TUser&gt;</c> (<c>AddIdentity</c>, <c>AddDefaultIdentity</c> or
/// <c>AddIdentityCore</c>), SSO finds users with <c>FindByEmailAsync</c> and, with
/// <c>Sso.CreateUsers</c> on, creates them with no password; with
/// <c>SignInManager&lt;TUser&gt;</c> registered too, it signs them in through it. Host
/// hooks on <see cref="Configuration.SsoOptions"/> always win over these.
/// </summary>
internal interface ISsoIdentityUsers
{
    /// <summary>The Identity user id for an email (case-insensitive), or null.</summary>
    Task<string?> FindByEmailAsync(IServiceProvider services, string email, CancellationToken ct);

    /// <summary>Creates a user with this email and no password, and returns its id (null when Identity refused).</summary>
    Task<string?> CreateAsync(IServiceProvider services, string email, string name, CancellationToken ct);

    /// <summary>Signs the user in through <c>SignInManager</c>; false when there is none.</summary>
    Task<bool> SignInAsync(HttpContext context, string userId);
}

/// <summary>
/// Finds the host's Identity user type from the service collection the first time SSO
/// needs it, so it works whether the host called <c>AddIdentity</c> before or after
/// <c>AddEscalated</c>.
/// </summary>
internal sealed class SsoIdentityLocator
{
    private readonly IServiceCollection _services;
    private readonly Lazy<ISsoIdentityUsers?> _users;

    public SsoIdentityLocator(IServiceCollection services)
    {
        _services = services;
        _users = new Lazy<ISsoIdentityUsers?>(Locate);
    }

    /// <summary>The Identity bridge, or null when the host has no (or more than one) Identity user type.</summary>
    public ISsoIdentityUsers? Users => _users.Value;

    private ISsoIdentityUsers? Locate()
    {
        var userTypes = _services
            .Select(d => d.ServiceType)
            .Where(t => t.IsConstructedGenericType && t.GetGenericTypeDefinition() == typeof(UserManager<>))
            .Select(t => t.GetGenericArguments()[0])
            .Distinct()
            .ToList();
        if (userTypes.Count != 1)
        {
            // None, or more than one: do not guess which users SSO signs in.
            return null;
        }

        var userType = userTypes[0];
        var hasSignIn = _services.Any(d => d.ServiceType == typeof(SignInManager<>).MakeGenericType(userType));
        return (ISsoIdentityUsers)Activator.CreateInstance(typeof(SsoIdentityUsers<>).MakeGenericType(userType), new object[] { hasSignIn })!;
    }
}

internal sealed class SsoIdentityUsers<TUser> : ISsoIdentityUsers where TUser : class
{
    private readonly bool _hasSignIn;

    public SsoIdentityUsers(bool hasSignIn) => _hasSignIn = hasSignIn;

    public async Task<string?> FindByEmailAsync(IServiceProvider services, string email, CancellationToken ct)
    {
        var users = services.GetRequiredService<UserManager<TUser>>();
        if (!users.SupportsUserEmail)
        {
            return null;
        }

        // FindByEmailAsync compares normalized (upper-cased) emails: case-insensitive.
        var user = await users.FindByEmailAsync(email);
        return user is null ? null : await users.GetUserIdAsync(user);
    }

    public async Task<string?> CreateAsync(IServiceProvider services, string email, string name, CancellationToken ct)
    {
        var users = services.GetRequiredService<UserManager<TUser>>();
        if (!users.SupportsUserEmail || typeof(TUser).GetConstructor(Type.EmptyTypes) is null)
        {
            return null;
        }

        var user = (TUser)Activator.CreateInstance(typeof(TUser))!;
        await users.SetUserNameAsync(user, email);
        await users.SetEmailAsync(user, email);
        // No password: the account can only sign in through SSO (or after a password reset).
        var created = await users.CreateAsync(user);
        return created.Succeeded ? await users.GetUserIdAsync(user) : null;
    }

    public async Task<bool> SignInAsync(HttpContext context, string userId)
    {
        if (!_hasSignIn)
        {
            return false;
        }

        var users = context.RequestServices.GetRequiredService<UserManager<TUser>>();
        var user = await users.FindByIdAsync(userId);
        if (user is null)
        {
            // An id from a host FindUserByEmail/CreateUser hook that Identity does not know.
            return false;
        }

        await context.RequestServices.GetRequiredService<SignInManager<TUser>>().SignInAsync(user, isPersistent: false);
        return true;
    }
}
