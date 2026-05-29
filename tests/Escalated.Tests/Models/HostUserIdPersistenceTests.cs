using Escalated.Data;
using Escalated.Enums;
using Escalated.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Escalated.Tests.Models;

public class HostUserIdPersistenceTests
{
    [Fact]
    public async Task Ticket_AssignedTo_RoundTripsUuidStyleHostUserId()
    {
        const string agentId = "550e8400-e29b-41d4-a716-446655440000";
        var dbName = Guid.NewGuid().ToString();

        await using (var db = CreateSqliteDb(dbName))
        {
            db.Tickets.Add(new Ticket
            {
                Reference = "ESC-UUID-1",
                Subject = "UUID assignee",
                Status = TicketStatus.Open,
                Priority = TicketPriority.Medium,
                AssignedTo = agentId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await using (var db = CreateSqliteDb(dbName))
        {
            var ticket = await db.Tickets.SingleAsync();
            Assert.Equal(agentId, ticket.AssignedTo);
        }
    }

    private static EscalatedDbContext CreateSqliteDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<EscalatedDbContext>()
            .UseSqlite($"Data Source={dbName}")
            .Options;

        var db = new EscalatedDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }
}
