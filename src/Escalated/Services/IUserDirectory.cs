namespace Escalated.Services;

/// <summary>
/// Host-supplied lookup of the host application's user table. The Escalated
/// plugin does not own a User entity (auth lives in the host app), but the
/// admin users-management page needs to surface a paged list of users and
/// look one up by id to flip its admin / agent role.
///
/// Hosts register their own implementation against this interface when they
/// call <c>AddEscalated</c>; a no-op default ships so the plugin boots even
/// before the host wires it up — the users-management page will then show
/// an empty list rather than crashing.
///
/// Mirrors the host User-table reads in escalated-laravel's
/// <c>Admin/UserController</c> (PR #94) — the canonical reference.
/// </summary>
public interface IUserDirectory
{
    /// <summary>
    /// Returns a page of users matching the optional search term against
    /// name and email. Caller controls page and pageSize; implementation
    /// owns the sort (admins first, then agents, then by id ascending —
    /// match the shape the shared Vue page expects).
    /// </summary>
    Task<UserDirectoryPage> ListAsync(string? search, int page, int pageSize, CancellationToken ct = default);

    /// <summary>Looks up a single user by id. Returns null when no match.</summary>
    Task<UserDirectoryEntry?> FindAsync(int id, CancellationToken ct = default);
}

/// <summary>Row shape consumed by <c>Escalated/Admin/Users/Index</c>.</summary>
public record UserDirectoryEntry(int Id, string? Name, string? Email);

/// <summary>Paginator wrapper aligned with Laravel-style paginate() output.</summary>
public record UserDirectoryPage(
    IReadOnlyList<UserDirectoryEntry> Items,
    int Total,
    int Page,
    int PerPage);

/// <summary>
/// Default no-op directory: returns an empty page and null lookups. Hosts that
/// want the admin users-management page to surface real users must register
/// their own <see cref="IUserDirectory"/> against the DI container.
/// </summary>
public class NullUserDirectory : IUserDirectory
{
    public Task<UserDirectoryPage> ListAsync(string? search, int page, int pageSize, CancellationToken ct = default)
        => Task.FromResult(new UserDirectoryPage(Array.Empty<UserDirectoryEntry>(), 0, page, pageSize));

    public Task<UserDirectoryEntry?> FindAsync(int id, CancellationToken ct = default)
        => Task.FromResult<UserDirectoryEntry?>(null);
}
