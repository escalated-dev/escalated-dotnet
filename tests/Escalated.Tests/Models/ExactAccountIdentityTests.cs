using Escalated.Authorization;
using Escalated.Data;
using Escalated.Models;
using Escalated.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Escalated.Tests.Models;

public class ExactAccountIdentityTests
{
    [Theory]
    [InlineData("Company ", "001")]
    [InlineData("Company", "001 ")]
    public async Task AccountQueries_PreserveSpacesUnderPaddingCollations(string type, string id)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        // Model SQL Server's ANSI padding comparison while retaining case sensitivity.
        connection.CreateCollation("BINARY", (left, right) => string.CompareOrdinal(left?.TrimEnd(' '), right?.TrimEnd(' ')));
        await using var db = new EscalatedDbContext(new DbContextOptionsBuilder<EscalatedDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var exact = new Ticket { Reference = "EXACT", Subject = "Exact", AccountType = "Company", AccountId = "001" };
        var padded = new Ticket { Reference = "PADDED", Subject = "Padded", AccountType = type, AccountId = id };
        db.Tickets.AddRange(exact, padded);
        await db.SaveChangesAsync();
        const string account = "{\"type\":\"Company\",\"id\":\"001\"}";
        Assert.Equal(new[] { exact.Id }, await TicketRoutingConditions.WhereAccount(db.Tickets, account).Select(t => t.Id).ToArrayAsync());
        Assert.Equal(new[] { padded.Id }, await TicketRoutingConditions.WhereAccount(db.Tickets, account, "not_equals").Select(t => t.Id).ToArrayAsync());
        var actor = new TicketAccessActor("agent", false, true, new TicketAccount("Company", "001"));
        Assert.Equal(new[] { exact.Id }, await TicketAccessScope.RunAsync(actor, () => db.Tickets.Select(t => t.Id).ToArrayAsync()));
    }
}
