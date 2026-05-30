using Escalated.Configuration;
using Escalated.Contracts;
using Escalated.Data;
using Escalated.Dtos;
using Escalated.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Escalated.Services;

public class TicketSubjectService
{
    private readonly EscalatedDbContext _db;
    private readonly ITicketSubjectResolver _resolver;
    private readonly EscalatedOptions _options;

    public TicketSubjectService(
        EscalatedDbContext db,
        ITicketSubjectResolver resolver,
        IOptions<EscalatedOptions> options)
    {
        _db = db;
        _resolver = resolver;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<TicketSubjectLink>> ListAsync(Ticket ticket, CancellationToken ct = default)
    {
        return await _db.TicketSubjectLinks
            .Where(l => l.TicketId == ticket.Id)
            .OrderBy(l => l.Position)
            .ThenBy(l => l.Id)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Attaches a subject (idempotent on ticket+type+id). When <paramref name="viaApi"/> is
    /// true, the type must appear in <see cref="TicketSubjectsOptions.Types"/> and that
    /// list must be non-empty.
    /// </summary>
    public async Task<TicketSubjectLink> AttachAsync(
        Ticket ticket,
        string subjectType,
        string subjectId,
        string? role = null,
        int? position = null,
        bool viaApi = false,
        CancellationToken ct = default)
    {
        EnsureTypeAllowed(subjectType, viaApi);

        subjectId = subjectId.Trim();
        if (subjectId.Length == 0)
            throw new ArgumentException("Subject id is required.", nameof(subjectId));

        var existing = await _db.TicketSubjectLinks
            .FirstOrDefaultAsync(
                l => l.TicketId == ticket.Id
                  && l.SubjectType == subjectType
                  && l.SubjectId == subjectId,
                ct);

        var now = DateTime.UtcNow;
        if (existing != null)
        {
            existing.Role = role;
            if (position.HasValue)
                existing.Position = position.Value;
            existing.UpdatedAt = now;
            await _db.SaveChangesAsync(ct);
            return existing;
        }

        if (!position.HasValue)
        {
            var maxPosition = await _db.TicketSubjectLinks
                .Where(l => l.TicketId == ticket.Id)
                .Select(l => (int?)l.Position)
                .MaxAsync(ct);
            position = (maxPosition ?? -1) + 1;
        }

        var link = new TicketSubjectLink
        {
            TicketId = ticket.Id,
            SubjectType = subjectType,
            SubjectId = subjectId,
            Role = role,
            Position = position.Value,
            CreatedAt = now,
            UpdatedAt = now,
        };

        _db.TicketSubjectLinks.Add(link);
        await _db.SaveChangesAsync(ct);
        return link;
    }

    /// <summary>Detaches by link row id. Returns rows removed (0 or 1).</summary>
    public async Task<int> DetachByLinkIdAsync(Ticket ticket, int linkId, CancellationToken ct = default)
    {
        var link = await _db.TicketSubjectLinks
            .FirstOrDefaultAsync(l => l.Id == linkId && l.TicketId == ticket.Id, ct);
        if (link == null)
            return 0;

        _db.TicketSubjectLinks.Remove(link);
        await _db.SaveChangesAsync(ct);
        return 1;
    }

    /// <summary>Detaches by type+id. Returns rows removed (0 or 1).</summary>
    public async Task<int> DetachAsync(
        Ticket ticket,
        string subjectType,
        string subjectId,
        CancellationToken ct = default)
    {
        var link = await _db.TicketSubjectLinks
            .FirstOrDefaultAsync(
                l => l.TicketId == ticket.Id
                  && l.SubjectType == subjectType
                  && l.SubjectId == subjectId,
                ct);
        if (link == null)
            return 0;

        _db.TicketSubjectLinks.Remove(link);
        await _db.SaveChangesAsync(ct);
        return 1;
    }

    /// <summary>Replaces all subjects, preserving caller order.</summary>
    public async Task SyncAsync(
        Ticket ticket,
        IReadOnlyList<(string Type, string Id, string? Role)> subjects,
        CancellationToken ct = default)
    {
        var existing = await _db.TicketSubjectLinks
            .Where(l => l.TicketId == ticket.Id)
            .ToListAsync(ct);
        _db.TicketSubjectLinks.RemoveRange(existing);

        var position = 0;
        var now = DateTime.UtcNow;
        foreach (var (type, id, role) in subjects)
        {
            EnsureTypeAllowed(type, viaApi: false);
            _db.TicketSubjectLinks.Add(new TicketSubjectLink
            {
                TicketId = ticket.Id,
                SubjectType = type,
                SubjectId = id,
                Role = role,
                Position = position++,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<TicketSubjectResponse>> SerializeAsync(
        IEnumerable<TicketSubjectLink> links,
        CancellationToken ct = default)
    {
        var result = new List<TicketSubjectResponse>();
        foreach (var link in links.OrderBy(l => l.Position).ThenBy(l => l.Id))
        {
            var subject = await _resolver.ResolveAsync(link.SubjectType, link.SubjectId, ct);
            result.Add(BuildResponse(link, subject));
        }

        return result;
    }

    public async Task PopulateTicketSubjectsAsync(Ticket ticket, CancellationToken ct = default)
    {
        var links = ticket.Subjects?.Count > 0
            ? ticket.Subjects.OrderBy(l => l.Position).ThenBy(l => l.Id).ToList()
            : await ListAsync(ticket, ct);

        ticket.SubjectsPayload = await SerializeAsync(links, ct);
    }

    internal static TicketSubjectResponse BuildResponse(TicketSubjectLink link, ITicketSubject? subject)
    {
        if (subject != null)
        {
            return new TicketSubjectResponse(
                link.SubjectType,
                link.SubjectId,
                link.Role,
                subject.TicketSubjectTitle(),
                subject.TicketSubjectSubtitle(),
                subject.TicketSubjectUrl(),
                subject.TicketSubjectColor(),
                subject.TicketSubjectIcon(),
                Missing: false);
        }

        return new TicketSubjectResponse(
            link.SubjectType,
            link.SubjectId,
            link.Role,
            FallbackTitle(link.SubjectType, link.SubjectId),
            Subtitle: null,
            Url: null,
            Color: null,
            Icon: null,
            Missing: true);
    }

    internal static string FallbackTitle(string subjectType, string subjectId)
        => $"{ShortTypeName(subjectType)}#{subjectId}";

    internal static string ShortTypeName(string subjectType)
    {
        var idx = subjectType.LastIndexOf('.');
        return idx >= 0 ? subjectType[(idx + 1)..] : subjectType;
    }

    private void EnsureTypeAllowed(string subjectType, bool viaApi)
    {
        var allowed = _options.TicketSubjects.Types;
        if (viaApi)
        {
            if (allowed.Count == 0 || !allowed.Contains(subjectType, StringComparer.Ordinal))
                throw new InvalidOperationException($"Subject type [{subjectType}] is not an allowed ticket subject.");
            return;
        }

        if (allowed.Count > 0 && !allowed.Contains(subjectType, StringComparer.Ordinal))
            throw new InvalidOperationException($"Subject type [{subjectType}] is not an allowed ticket subject.");
    }
}
