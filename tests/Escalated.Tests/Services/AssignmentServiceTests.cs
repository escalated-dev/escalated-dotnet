using Escalated.Enums;
using Escalated.Events;
using Escalated.Models;
using Escalated.Services;
using Moq;
using Xunit;

namespace Escalated.Tests.Services;

public class AssignmentServiceTests
{
    [Fact]
    public async Task AssignAsync_SetsAssignedTo()
    {
        var db = TestHelpers.CreateInMemoryDb();
        var events = TestHelpers.MockEventDispatcher();
        var ticketService = new TicketService(db, events.Object, TestHelpers.DefaultOptions());
        var service = new AssignmentService(db, events.Object, ticketService);

        var ticket = new Ticket
        {
            Subject = "Assign Me",
            Reference = "ESC-00001",
            Status = TicketStatus.Open,
            Priority = TicketPriority.Medium,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        var assigned = await service.AssignAsync(ticket, 42, 1);

        Assert.Equal(42, assigned.AssignedTo);
        events.Verify(e => e.DispatchAsync(It.IsAny<TicketAssignedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UnassignAsync_ClearsAssignment()
    {
        var db = TestHelpers.CreateInMemoryDb();
        var events = TestHelpers.MockEventDispatcher();
        var ticketService = new TicketService(db, events.Object, TestHelpers.DefaultOptions());
        var service = new AssignmentService(db, events.Object, ticketService);

        var ticket = new Ticket
        {
            Subject = "Unassign Me",
            Reference = "ESC-00002",
            Status = TicketStatus.Open,
            Priority = TicketPriority.Medium,
            AssignedTo = 42,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        var unassigned = await service.UnassignAsync(ticket, 1);

        Assert.Null(unassigned.AssignedTo);
        events.Verify(e => e.DispatchAsync(It.IsAny<TicketUnassignedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAgentWorkloadAsync_ReturnsCorrectCounts()
    {
        var db = TestHelpers.CreateInMemoryDb();
        var events = TestHelpers.MockEventDispatcher();
        var ticketService = new TicketService(db, events.Object, TestHelpers.DefaultOptions());
        var service = new AssignmentService(db, events.Object, ticketService);

        db.Tickets.Add(new Ticket
        {
            Subject = "Open 1",
            Reference = "ESC-00001",
            Status = TicketStatus.Open,
            Priority = TicketPriority.Medium,
            AssignedTo = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.Tickets.Add(new Ticket
        {
            Subject = "Open 2",
            Reference = "ESC-00002",
            Status = TicketStatus.InProgress,
            Priority = TicketPriority.Medium,
            AssignedTo = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.Tickets.Add(new Ticket
        {
            Subject = "Resolved",
            Reference = "ESC-00003",
            Status = TicketStatus.Resolved,
            Priority = TicketPriority.Medium,
            AssignedTo = 1,
            ResolvedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var workload = await service.GetAgentWorkloadAsync(1);

        Assert.Equal(2, workload.Open);
        Assert.Equal(1, workload.ResolvedToday);
    }
}
