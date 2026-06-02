using Escalated.Contracts;

namespace Escalated.Services;

/// <summary>
/// Host-supplied resolver from stored <c>(subject_type, subject_id)</c> to a
/// presentation model. The Escalated package does not own host entities.
/// </summary>
public interface ITicketSubjectResolver
{
    Task<ITicketSubject?> ResolveAsync(string subjectType, string subjectId, CancellationToken ct = default);
}

/// <summary>
/// Default no-op resolver — subjects serialize with <c>missing: true</c> and a
/// <c>type#id</c> fallback title until the host registers its own implementation.
/// </summary>
public class NullTicketSubjectResolver : ITicketSubjectResolver
{
    public Task<ITicketSubject?> ResolveAsync(string subjectType, string subjectId, CancellationToken ct = default)
        => Task.FromResult<ITicketSubject?>(null);
}
