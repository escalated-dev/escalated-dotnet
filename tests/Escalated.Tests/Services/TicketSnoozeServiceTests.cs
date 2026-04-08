using Escalated.Enums;
using Escalated.Models;
using Escalated.Services;
using Xunit;

namespace Escalated.Tests.Services;

public class TicketSnoozeServiceTests
{
    [Fact]
    public async Task SnoozeAsync_SetsSnoozedUntil()
    {
        var db = TestHelpers.CreateInMemoryDb();
        var events = TestHelpers.MockEventDispatcher();
        var ticketService = new TicketService(db, events.Object, TestHelpers.DefaultOptions());
        var service = new TicketSnoozeService(db, ticketService);

        var ticket = new Ticket
        {
            Subject = "Snooze Me",
            Reference = "ESC-00001",
            Status = TicketStatus.Open,
            Priority = TicketPriority.Medium,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        var snoozeUntil = DateTime.UtcNow.AddDays(1);
        var snoozed = await service.SnoozeAsync(ticket, snoozeUntil, 1);

        Assert.Equal(snoozeUntil, snoozed.SnoozedUntil);
    }

    [Fact]
    public async Task UnsnoozeAsync_ClearsSnoozedUntil()
    {
        var db = TestHelpers.CreateInMemoryDb();
        var events = TestHelpers.MockEventDispatcher();
        var ticketService = new TicketService(db, events.Object, TestHelpers.DefaultOptions());
        var service = new TicketSnoozeService(db, ticketService);

        var ticket = new Ticket
        {
            Subject = "Snooze Me",
            Reference = "ESC-00002",
            Status = TicketStatus.Open,
            Priority = TicketPriority.Medium,
            SnoozedUntil = DateTime.UtcNow.AddDays(1),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();

        var unsnoozed = await service.UnsnoozeAsync(ticket, 1);

        Assert.Null(unsnoozed.SnoozedUntil);
    }

    [Fact]
    public async Task WakeExpiredAsync_UnsnoozesPastDueTickets()
    {
        var db = TestHelpers.CreateInMemoryDb();
        var events = TestHelpers.MockEventDispatcher();
        var ticketService = new TicketService(db, events.Object, TestHelpers.DefaultOptions());
        var service = new TicketSnoozeService(db, ticketService);

        // Past due snooze
        db.Tickets.Add(new Ticket
        {
            Subject = "Expired Snooze",
            Reference = "ESC-00003",
            Status = TicketStatus.Open,
            Priority = TicketPriority.Medium,
            SnoozedUntil = DateTime.UtcNow.AddHours(-1),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        // Future snooze (should not be woken)
        db.Tickets.Add(new Ticket
        {
            Subject = "Future Snooze",
            Reference = "ESC-00004",
            Status = TicketStatus.Open,
            Priority = TicketPriority.Medium,
            SnoozedUntil = DateTime.UtcNow.AddDays(1),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        var woken = await service.WakeExpiredAsync();

        Assert.Equal(1, woken);
    }
}
