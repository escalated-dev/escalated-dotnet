using Escalated.Data;
using Escalated.Enums;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Services;

/// <summary>What one purge run deleted, or on a dry run would delete.</summary>
public sealed record RetentionPurgeReport(
    bool DryRun,
    bool Force,
    int ClosedTickets,
    int Attachments,
    int AuditLogs,
    int GraceExpiredTickets,
    int GuestVerifications);

/// <summary>
/// Applies the data retention settings the admin Data retention page saves
/// (<c>retention_closed_tickets</c>, <c>retention_attachments</c>,
/// <c>retention_audit_logs</c>), as escalated-laravel's
/// <c>escalated:purge-expired</c>. Each period defaults to <c>never</c>, which
/// purges nothing.
///
/// <para>Closed tickets past their period are soft-deleted (<c>DeletedAt</c>);
/// with <c>force</c> they are deleted outright. Soft-deleted tickets older than
/// the 30-day grace period are deleted outright. Attachment rows and audit log
/// rows past their periods are deleted (attachment files stay where
/// <see cref="Email.Inbound.IAttachmentStorage"/> put them: it has no delete).
/// Expired guest verification codes are deleted.</para>
/// </summary>
public class DataRetentionService
{
    /// <summary>How long a soft-deleted ticket is kept before it is deleted for good.</summary>
    public static readonly TimeSpan GracePeriod = TimeSpan.FromDays(30);

    private const int Batch = 200;

    private static readonly Dictionary<string, int> DaysMap = new(StringComparer.Ordinal)
    {
        ["90_days"] = 90,
        ["180_days"] = 180,
        ["365_days"] = 365,
        ["1_year"] = 365,
        ["2_years"] = 730,
        ["3_years"] = 1095,
        ["5_years"] = 1825,
    };

    private readonly EscalatedDbContext _db;
    private readonly SettingsService _settings;

    public DataRetentionService(EscalatedDbContext db, SettingsService settings)
    {
        _db = db;
        _settings = settings;
    }

    /// <summary>The period in days for a setting value; null for <c>never</c> or anything unknown.</summary>
    public static int? Days(string? setting) =>
        setting is not null && DaysMap.TryGetValue(setting, out var days) ? days : null;

    private async Task<DateTime?> CutoffAsync(string key, DateTime now, CancellationToken ct)
    {
        var days = Days(await _settings.GetAsync(key, "never", ct));
        return days is null ? null : now.AddDays(-days.Value);
    }

    /// <summary>
    /// What the periods would purge now: the page's <c>purgePreview</c>. Never
    /// throws; a count it cannot make is 0, as in Laravel.
    /// </summary>
    public async Task<(int Tickets, int Attachments, int AuditLogs)> PreviewAsync(DateTime? now = null, CancellationToken ct = default)
    {
        var at = now ?? DateTime.UtcNow;
        int tickets = 0, attachments = 0, auditLogs = 0;
        try
        {
            if (await CutoffAsync("retention_closed_tickets", at, ct) is { } ticketCutoff)
            {
                tickets = await ExpiredTickets(ticketCutoff).CountAsync(ct);
            }

            if (await CutoffAsync("retention_attachments", at, ct) is { } attachmentCutoff)
            {
                attachments = await _db.Attachments.IgnoreQueryFilters().CountAsync(a => a.CreatedAt < attachmentCutoff, ct);
            }

            if (await CutoffAsync("retention_audit_logs", at, ct) is { } auditCutoff)
            {
                auditLogs = await _db.AuditLogs.CountAsync(a => a.CreatedAt < auditCutoff, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Tables may not exist yet.
        }

        return (tickets, attachments, auditLogs);
    }

    private IQueryable<Models.Ticket> ExpiredTickets(DateTime cutoff) =>
        _db.Tickets.IgnoreQueryFilters()
            .Where(t => t.Status == TicketStatus.Closed && t.ClosedAt != null && t.ClosedAt < cutoff && t.DeletedAt == null);

    /// <summary>Runs the purge. With <paramref name="dryRun"/> it only counts.</summary>
    public async Task<RetentionPurgeReport> PurgeExpiredAsync(bool dryRun = false, bool force = false, DateTime? now = null,
        CancellationToken ct = default)
    {
        var at = now ?? DateTime.UtcNow;

        var closed = 0;
        if (await CutoffAsync("retention_closed_tickets", at, ct) is { } ticketCutoff)
        {
            closed = await ExpiredTickets(ticketCutoff).CountAsync(ct);
            if (!dryRun && closed > 0)
            {
                if (force)
                {
                    await DeleteTicketsAsync(() => ExpiredTickets(ticketCutoff), ct);
                }
                else
                {
                    await ExpiredTickets(ticketCutoff).ExecuteUpdateAsync(s => s.SetProperty(t => t.DeletedAt, at), ct);
                }
            }
        }

        var attachments = 0;
        if (await CutoffAsync("retention_attachments", at, ct) is { } attachmentCutoff)
        {
            var expired = _db.Attachments.IgnoreQueryFilters().Where(a => a.CreatedAt < attachmentCutoff);
            attachments = await expired.CountAsync(ct);
            if (!dryRun && attachments > 0) await expired.ExecuteDeleteAsync(ct);
        }

        var auditLogs = 0;
        if (await CutoffAsync("retention_audit_logs", at, ct) is { } auditCutoff)
        {
            var expired = _db.AuditLogs.Where(a => a.CreatedAt < auditCutoff);
            auditLogs = await expired.CountAsync(ct);
            if (!dryRun && auditLogs > 0) await expired.ExecuteDeleteAsync(ct);
        }

        var graceCutoff = at - GracePeriod;
        IQueryable<Models.Ticket> GraceExpired() =>
            _db.Tickets.IgnoreQueryFilters().Where(t => t.DeletedAt != null && t.DeletedAt < graceCutoff);
        var grace = await GraceExpired().CountAsync(ct);
        if (!dryRun && grace > 0) await DeleteTicketsAsync(GraceExpired, ct);

        var codes = _db.GuestVerifications.Where(v => v.ExpiresAt <= at);
        var guestCodes = await codes.CountAsync(ct);
        if (!dryRun && guestCodes > 0) await codes.ExecuteDeleteAsync(ct);

        return new RetentionPurgeReport(dryRun, force, closed, attachments, auditLogs, grace, guestCodes);
    }

    /// <summary>
    /// Deletes the tickets a query selects, a batch per transaction. Most rows that
    /// point at a ticket go by the database's cascade; the ones that would block the
    /// delete or have no foreign key (polymorphic attachments and custom field
    /// values, links where it is the child, tickets merged into it, inbound emails
    /// that created it) are handled first.
    /// </summary>
    private async Task DeleteTicketsAsync(Func<IQueryable<Models.Ticket>> query, CancellationToken ct)
    {
        while (true)
        {
            var ids = await query().OrderBy(t => t.Id).Select(t => t.Id).Take(Batch).ToListAsync(ct);
            if (ids.Count == 0) return;

            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            var replyIds = await _db.Replies.IgnoreQueryFilters().Where(r => ids.Contains(r.TicketId)).Select(r => r.Id).ToListAsync(ct);

            await _db.Attachments.IgnoreQueryFilters()
                .Where(a => (a.AttachableType.ToLower() == "ticket" && ids.Contains(a.AttachableId))
                    || (a.AttachableType.ToLower() == "reply" && replyIds.Contains(a.AttachableId)))
                .ExecuteDeleteAsync(ct);
            await _db.CustomFieldValues.IgnoreQueryFilters()
                .Where(v => v.EntityType.ToLower() == "ticket" && ids.Contains(v.EntityId))
                .ExecuteDeleteAsync(ct);
            await _db.TicketLinks.IgnoreQueryFilters()
                .Where(l => ids.Contains(l.ParentTicketId) || ids.Contains(l.ChildTicketId))
                .ExecuteDeleteAsync(ct);
            await _db.TicketFollowers.IgnoreQueryFilters().Where(f => ids.Contains(f.TicketId)).ExecuteDeleteAsync(ct);
            await _db.SatisfactionRatings.IgnoreQueryFilters().Where(r => ids.Contains(r.TicketId)).ExecuteDeleteAsync(ct);
            await _db.InboundEmails
                .Where(i => i.TicketId != null && ids.Contains(i.TicketId.Value))
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.TicketId, (int?)null).SetProperty(i => i.ReplyId, (int?)null), ct);
            await _db.Tickets.IgnoreQueryFilters()
                .Where(t => t.MergedIntoId != null && ids.Contains(t.MergedIntoId.Value))
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.MergedIntoId, (int?)null), ct);
            await _db.Tickets.IgnoreQueryFilters().Where(t => ids.Contains(t.Id)).ExecuteDeleteAsync(ct);

            await transaction.CommitAsync(ct);
        }
    }
}
