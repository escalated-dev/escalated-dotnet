using Escalated.Data;
using Escalated.Enums;
using Escalated.Models;
using Escalated.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Escalated.Tests.Services;

/// <summary>
/// <see cref="DataRetentionService"/> against escalated-laravel's
/// <c>escalated:purge-expired</c>: each period defaults to never; closed tickets
/// past theirs are soft-deleted (deleted outright with --force); soft-deleted
/// tickets past the 30-day grace period go for good; attachments and audit logs
/// past theirs are deleted; expired guest codes are deleted; a dry run deletes
/// nothing.
/// </summary>
public class DataRetentionServiceTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    private static (EscalatedDbContext Db, DataRetentionService Service, SettingsService Settings) Create()
    {
        var db = TestHelpers.CreateDb();
        var settings = new SettingsService(db);
        return (db, new DataRetentionService(db, settings), settings);
    }

    private static async Task<Ticket> TicketAsync(EscalatedDbContext db, string reference, TicketStatus status, DateTime? closedAt,
        DateTime? deletedAt = null)
    {
        var ticket = new Ticket { Reference = reference, Subject = reference, Status = status, ClosedAt = closedAt, DeletedAt = deletedAt };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();
        var reply = new Reply { TicketId = ticket.Id, Body = "hello" };
        db.Replies.Add(reply);
        await db.SaveChangesAsync();
        db.Attachments.AddRange(
            new Attachment { AttachableType = "ticket", AttachableId = ticket.Id, Filename = "a.txt", Path = "a" },
            new Attachment { AttachableType = "reply", AttachableId = reply.Id, Filename = "b.txt", Path = "b" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return ticket;
    }

    /// <summary>Every row that points at a ticket, for the hard delete to clear.</summary>
    private static async Task AddDependentsAsync(EscalatedDbContext db, Ticket ticket, Ticket other)
    {
        var workflow = new Workflow { Name = "wf", TriggerEvent = "ticket.created", Conditions = "{}", Actions = "[]" };
        var field = new CustomField { Name = "Order", Slug = "order-" + ticket.Id };
        db.AddRange(workflow, field);
        await db.SaveChangesAsync();
        db.TicketFollowers.Add(new TicketFollower { TicketId = ticket.Id, UserId = "7" });
        db.SatisfactionRatings.Add(new SatisfactionRating { TicketId = ticket.Id, Rating = 5 });
        db.WorkflowLogs.Add(new WorkflowLog { WorkflowId = workflow.Id, TicketId = ticket.Id, TriggerEvent = "ticket.created" });
        db.SideConversations.Add(new SideConversation { TicketId = ticket.Id, Subject = "aside" });
        db.ChatSessions.Add(new ChatSession { TicketId = ticket.Id });
        db.TicketLinks.Add(new TicketLink { ParentTicketId = other.Id, ChildTicketId = ticket.Id });
        db.CustomFieldValues.Add(new CustomFieldValue { CustomFieldId = field.Id, EntityType = "ticket", EntityId = ticket.Id, Value = "v" });
        db.InboundEmails.Add(new InboundEmail { FromEmail = "a@example.com", TicketId = ticket.Id });
        await db.SaveChangesAsync();
        await db.Tickets.IgnoreQueryFilters().Where(t => t.Id == other.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.MergedIntoId, ticket.Id));
        db.ChangeTracker.Clear();
    }

    private static Task<bool> ExistsAsync(EscalatedDbContext db, int id) =>
        db.Tickets.IgnoreQueryFilters().AnyAsync(t => t.Id == id);

    private static Task<DateTime?> DeletedAtAsync(EscalatedDbContext db, int id) =>
        db.Tickets.IgnoreQueryFilters().Where(t => t.Id == id).Select(t => t.DeletedAt).SingleAsync();

    [Fact]
    public async Task Never_LeavesEverythingAlone()
    {
        var (db, service, _) = Create();
        await using var _db = db;
        var old = await TicketAsync(db, "OLD", TicketStatus.Closed, Now.AddYears(-6));
        db.AuditLogs.Add(new AuditLog { EntityType = "ticket", Action = "updated", CreatedAt = Now.AddYears(-6) });
        await db.SaveChangesAsync();

        var report = await service.PurgeExpiredAsync(now: Now);

        Assert.Equal(0, report.ClosedTickets);
        Assert.Null(await DeletedAtAsync(db, old.Id));
        Assert.Equal(2, await db.Attachments.IgnoreQueryFilters().CountAsync());
        Assert.Equal(1, await db.AuditLogs.CountAsync());
        Assert.Equal((0, 0, 0), await service.PreviewAsync(Now));
    }

    [Fact]
    public async Task ClosedTickets_PastTheirPeriodAreSoftDeleted_OthersKept()
    {
        var (db, service, settings) = Create();
        await using var _db = db;
        await settings.SetAsync("retention_closed_tickets", "1_year");
        var expired = await TicketAsync(db, "OLD", TicketStatus.Closed, Now.AddYears(-2));
        var recent = await TicketAsync(db, "NEW", TicketStatus.Closed, Now.AddMonths(-1));
        var open = await TicketAsync(db, "OPEN", TicketStatus.Open, Now.AddYears(-2));
        var resolved = await TicketAsync(db, "RES", TicketStatus.Resolved, Now.AddYears(-2));

        Assert.Equal(1, (await service.PreviewAsync(Now)).Tickets);

        var dry = await service.PurgeExpiredAsync(dryRun: true, now: Now);
        Assert.Equal(1, dry.ClosedTickets);
        Assert.Null(await DeletedAtAsync(db, expired.Id));

        var report = await service.PurgeExpiredAsync(now: Now);
        Assert.Equal(1, report.ClosedTickets);
        Assert.NotNull(await DeletedAtAsync(db, expired.Id));
        foreach (var kept in new[] { recent, open, resolved })
        {
            Assert.Null(await DeletedAtAsync(db, kept.Id));
        }

        // Soft-deleted tickets are not counted again.
        Assert.Equal(0, (await service.PreviewAsync(Now)).Tickets);
        Assert.Equal(0, (await service.PurgeExpiredAsync(now: Now)).ClosedTickets);
    }

    [Fact]
    public async Task Force_DeletesTheTicketWithEveryRowPointingAtIt()
    {
        var (db, service, settings) = Create();
        await using var _db = db;
        await settings.SetAsync("retention_closed_tickets", "1_year");
        var expired = await TicketAsync(db, "OLD", TicketStatus.Closed, Now.AddYears(-2));
        var kept = await TicketAsync(db, "KEEP", TicketStatus.Open, null);
        await AddDependentsAsync(db, expired, kept);

        var report = await service.PurgeExpiredAsync(force: true, now: Now);

        Assert.Equal(1, report.ClosedTickets);
        Assert.False(await ExistsAsync(db, expired.Id));
        Assert.True(await ExistsAsync(db, kept.Id));
        Assert.Null(await db.Tickets.IgnoreQueryFilters().Where(t => t.Id == kept.Id).Select(t => t.MergedIntoId).SingleAsync());
        Assert.Equal(0, await db.Replies.IgnoreQueryFilters().CountAsync(r => r.TicketId == expired.Id));
        Assert.Equal(1, await db.Replies.IgnoreQueryFilters().CountAsync(r => r.TicketId == kept.Id));
        Assert.Equal(2, await db.Attachments.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await db.TicketFollowers.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await db.SatisfactionRatings.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await db.WorkflowLogs.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await db.SideConversations.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await db.ChatSessions.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await db.TicketLinks.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await db.CustomFieldValues.IgnoreQueryFilters().CountAsync());
        Assert.Null(await db.InboundEmails.Select(i => i.TicketId).SingleAsync());
    }

    [Fact]
    public async Task SoftDeletedTickets_PastTheGracePeriodAreDeleted()
    {
        var (db, service, _) = Create();
        await using var _db = db;
        var stale = await TicketAsync(db, "STALE", TicketStatus.Closed, Now.AddYears(-2), deletedAt: Now.AddDays(-31));
        var fresh = await TicketAsync(db, "FRESH", TicketStatus.Closed, Now.AddYears(-2), deletedAt: Now.AddDays(-5));

        var dry = await service.PurgeExpiredAsync(dryRun: true, now: Now);
        Assert.Equal(1, dry.GraceExpiredTickets);
        Assert.True(await ExistsAsync(db, stale.Id));

        var report = await service.PurgeExpiredAsync(now: Now);
        Assert.Equal(1, report.GraceExpiredTickets);
        Assert.False(await ExistsAsync(db, stale.Id));
        Assert.True(await ExistsAsync(db, fresh.Id));
    }

    [Fact]
    public async Task Attachments_AuditLogs_AndGuestCodes_PastTheirCutoffsAreDeleted()
    {
        var (db, service, settings) = Create();
        await using var _db = db;
        await settings.SetAsync("retention_attachments", "1_year");
        await settings.SetAsync("retention_audit_logs", "90_days");
        var ticket = await TicketAsync(db, "T", TicketStatus.Open, null);
        await db.Attachments.IgnoreQueryFilters().Where(a => a.AttachableType == "ticket")
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.CreatedAt, Now.AddYears(-2)));
        db.AuditLogs.AddRange(
            new AuditLog { EntityType = "ticket", Action = "updated", CreatedAt = Now.AddDays(-100) },
            new AuditLog { EntityType = "ticket", Action = "updated", CreatedAt = Now.AddDays(-10) });
        db.GuestVerifications.AddRange(
            new GuestVerification { Id = "expired", Purpose = "create", ExpiresAt = Now.AddMinutes(-1) },
            new GuestVerification { Id = "live", Purpose = "create", ExpiresAt = Now.AddMinutes(10) });
        await db.SaveChangesAsync();

        Assert.Equal((0, 1, 1), await service.PreviewAsync(Now));

        var dry = await service.PurgeExpiredAsync(dryRun: true, now: Now);
        Assert.Equal((1, 1, 1), (dry.Attachments, dry.AuditLogs, dry.GuestVerifications));
        Assert.Equal(2, await db.Attachments.IgnoreQueryFilters().CountAsync());
        Assert.Equal(2, await db.AuditLogs.CountAsync());
        Assert.Equal(2, await db.GuestVerifications.CountAsync());

        var report = await service.PurgeExpiredAsync(now: Now);
        Assert.Equal((1, 1, 1), (report.Attachments, report.AuditLogs, report.GuestVerifications));
        Assert.Equal("reply", (await db.Attachments.IgnoreQueryFilters().SingleAsync()).AttachableType);
        Assert.Equal(1, await db.AuditLogs.CountAsync());
        Assert.Equal("live", (await db.GuestVerifications.SingleAsync()).Id);
        Assert.True(await ExistsAsync(db, ticket.Id));
    }

    [Theory]
    [InlineData("never", null)]
    [InlineData("", null)]
    [InlineData("bogus", null)]
    [InlineData("90_days", 90)]
    [InlineData("1_year", 365)]
    [InlineData("3_years", 1095)]
    [InlineData("5_years", 1825)]
    public void Days_MapsLaravelsValues(string setting, int? days) =>
        Assert.Equal(days, DataRetentionService.Days(setting));
}
