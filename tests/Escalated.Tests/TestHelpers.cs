using Escalated.Configuration;
using Escalated.Data;
using Escalated.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;

namespace Escalated.Tests;

public static class TestHelpers
{
    /// <summary>
    /// A database for a test to use.
    ///
    /// <para>SQLite unless ESCALATED_TEST_DATABASE says otherwise, so running the
    /// suite locally needs nothing installed. Both are real databases with real
    /// SQL, real types and real constraints; the EF Core InMemory provider these
    /// tests used before is none of those. It has no SQL to get wrong, so a
    /// column mapped to a type the database does not have, a constraint the
    /// model relies on, or a query the provider cannot translate all passed
    /// unnoticed.</para>
    ///
    /// <para>PostgreSQL is a server rather than a file, so each call gets a
    /// schema of its own. That isolates tests without a database each.</para>
    /// </summary>
    public static EscalatedDbContext CreateDb(string? dbName = null)
    {
        return TestDatabase.Create(dbName);
    }

    /// <summary>Kept for tests written against the old name.</summary>
    public static EscalatedDbContext CreateInMemoryDb(string? dbName = null)
    {
        return CreateDb(dbName);
    }

    public static IOptions<EscalatedOptions> DefaultOptions()
    {
        return Options.Create(new EscalatedOptions());
    }

    public static Mock<IEscalatedEventDispatcher> MockEventDispatcher()
    {
        var mock = new Mock<IEscalatedEventDispatcher>();
        mock.Setup(x => x.DispatchAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return mock;
    }
}
