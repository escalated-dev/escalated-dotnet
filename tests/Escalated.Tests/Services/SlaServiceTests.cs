using Escalated.Enums;
using Escalated.Events;
using Escalated.Models;
using Escalated.Services;
using Moq;
using Xunit;

namespace Escalated.Tests.Services;

public class SlaServiceTests
{
    [Fact]
    public async Task AttachPolicyAsync_SetsResponseAndResolutionDueDates()
    {
        var db = TestHelpers.CreateInMemoryDb();
        var events = TestHelpers.MockEventDispatcher();
        var service = new SlaService(db, events.Object, TestHelpers.DefaultOptions());

        var policy = new SlaPolicy
        {
            Name = "Standard",
            FirstResponseHours = """{"low":24,"medium":8,"high":4,"urgent":1,"critical":0.5}""",
            ResolutionHours = """{"low":72,"medium":24,"high":8,"urgent":4,"critical":2}""",
            IsActive = true,
            IsDefault = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.SlaPolicies.Add(policy);
        await db.SaveChangesAsync();

        var ticket = new Ticket
        {
            Subject = "SLA Test",
            Reference = "ESC-00001",
            Status = TicketStatus.Open,
            Priority = TicketPriority.High,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        await service.AttachPolicyAsync(ticket, policy);

        Assert.Equal(policy.Id, ticket.SlaPolicyId);
        Assert.NotNull(ticket.FirstResponseDueAt);
        Assert.NotNull(ticket.ResolutionDueAt);
        // High priority: 4h first response, 8h resolution
        Assert.True(ticket.FirstResponseDueAt > ticket.CreatedAt);
        Assert.True(ticket.ResolutionDueAt > ticket.FirstResponseDueAt);
    }

    [Fact]
    public async Task CheckBreachesAsync_FlagsBreach()
    {
        var db = TestHelpers.CreateInMemoryDb();
        var events = TestHelpers.MockEventDispatcher();
        var service = new SlaService(db, events.Object, TestHelpers.DefaultOptions());

        var ticket = new Ticket
        {
            Subject = "Breach Test",
            Reference = "ESC-00002",
            Status = TicketStatus.Open,
            Priority = TicketPriority.High,
            FirstResponseDueAt = DateTime.UtcNow.AddHours(-1), // Already past due
            SlaFirstResponseBreached = false,
            CreatedAt = DateTime.UtcNow.AddHours(-5),
            UpdatedAt = DateTime.UtcNow
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        var breached = await service.CheckBreachesAsync();

        Assert.Equal(1, breached);

        await db.Entry(ticket).ReloadAsync();
        Assert.True(ticket.SlaFirstResponseBreached);
        events.Verify(e => e.DispatchAsync(It.IsAny<SlaBreachedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CheckBreachesAsync_DoesNotDoubleBreach()
    {
        var db = TestHelpers.CreateInMemoryDb();
        var events = TestHelpers.MockEventDispatcher();
        var service = new SlaService(db, events.Object, TestHelpers.DefaultOptions());

        var ticket = new Ticket
        {
            Subject = "Already Breached",
            Reference = "ESC-00003",
            Status = TicketStatus.Open,
            Priority = TicketPriority.Medium,
            FirstResponseDueAt = DateTime.UtcNow.AddHours(-1),
            SlaFirstResponseBreached = true, // Already flagged
            CreatedAt = DateTime.UtcNow.AddHours(-10),
            UpdatedAt = DateTime.UtcNow
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        var breached = await service.CheckBreachesAsync();

        Assert.Equal(0, breached);
    }

    [Fact]
    public async Task CheckWarningsAsync_ReportsUpcomingBreaches()
    {
        var db = TestHelpers.CreateInMemoryDb();
        var events = TestHelpers.MockEventDispatcher();
        var service = new SlaService(db, events.Object, TestHelpers.DefaultOptions());

        var ticket = new Ticket
        {
            Subject = "Warning Test",
            Reference = "ESC-00004",
            Status = TicketStatus.Open,
            Priority = TicketPriority.Medium,
            FirstResponseDueAt = DateTime.UtcNow.AddMinutes(15), // Within 30min window
            SlaFirstResponseBreached = false,
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            UpdatedAt = DateTime.UtcNow
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        var warned = await service.CheckWarningsAsync(30);

        Assert.Equal(1, warned);
        events.Verify(e => e.DispatchAsync(It.IsAny<SlaWarningEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecordFirstResponseAsync_SetsTimestamp()
    {
        var db = TestHelpers.CreateInMemoryDb();
        var events = TestHelpers.MockEventDispatcher();
        var service = new SlaService(db, events.Object, TestHelpers.DefaultOptions());

        var ticket = new Ticket
        {
            Subject = "First Response",
            Reference = "ESC-00005",
            Status = TicketStatus.Open,
            Priority = TicketPriority.Medium,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        await service.RecordFirstResponseAsync(ticket);

        Assert.NotNull(ticket.FirstResponseAt);
    }

    [Fact]
    public async Task RecordFirstResponseAsync_NoDuplicateRecord()
    {
        var db = TestHelpers.CreateInMemoryDb();
        var events = TestHelpers.MockEventDispatcher();
        var service = new SlaService(db, events.Object, TestHelpers.DefaultOptions());

        var originalTime = DateTime.UtcNow.AddMinutes(-5);
        var ticket = new Ticket
        {
            Subject = "No Duplicate",
            Reference = "ESC-00006",
            Status = TicketStatus.Open,
            Priority = TicketPriority.Medium,
            FirstResponseAt = originalTime,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        await service.RecordFirstResponseAsync(ticket);

        Assert.Equal(originalTime, ticket.FirstResponseAt);
    }
}
