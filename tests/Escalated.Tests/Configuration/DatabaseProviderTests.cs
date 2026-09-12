using Escalated.Tests;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Escalated.Tests.Configuration;

/// <summary>
/// The suite runs on the database it was told to run on.
///
/// <para>A CI matrix leg that quietly fell back to SQLite would go green having
/// tested nothing the matrix exists to test, and the failure mode is invisible:
/// every other assertion in the suite still passes. This is the one test that
/// notices.</para>
/// </summary>
public class DatabaseProviderTests
{
    [Fact]
    public void RunsOnTheProviderTheEnvironmentAskedFor()
    {
        var requested = Environment.GetEnvironmentVariable("ESCALATED_TEST_DATABASE");
        requested = string.IsNullOrWhiteSpace(requested) ? "sqlite" : requested;

        using var db = TestHelpers.CreateDb();

        var expected = requested == "sqlite"
            ? "Microsoft.EntityFrameworkCore.Sqlite"
            : "Npgsql.EntityFrameworkCore.PostgreSQL";

        Assert.Equal(expected, db.Database.ProviderName);
    }

    [Fact]
    public void ReachesADatabaseItCanActuallyQuery()
    {
        // ProviderName reads configuration, not a socket. Without this, a leg
        // pointed at a server that never came up would still pass the check
        // above.
        using var db = TestHelpers.CreateDb();

        Assert.True(db.Database.CanConnect());
        Assert.Empty(db.Tickets.ToList());
    }

    [Fact]
    public void AnUnrecognisedProviderIsRefusedRatherThanDefaulted()
    {
        // The fallback is the danger, not the typo. A misspelt provider that
        // quietly became SQLite is exactly the silent-green this file exists to
        // prevent.
        var previous = Environment.GetEnvironmentVariable("ESCALATED_TEST_DATABASE");

        try
        {
            Environment.SetEnvironmentVariable("ESCALATED_TEST_DATABASE", "postgresql");

            var error = Assert.Throws<InvalidOperationException>(() => TestDatabase.Provider);
            Assert.Contains("postgresql", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ESCALATED_TEST_DATABASE", previous);
        }
    }

    /// <summary>
    /// The EF Core InMemory provider these tests used before is not a database:
    /// no SQL, no types, no constraints. It let a delivery row point at a
    /// webhook that did not exist and a mention violate a unique index, both of
    /// which a real database refuses.
    /// </summary>
    [Fact]
    public void TheSuiteRunsOnARelationalDatabase()
    {
        using var db = TestHelpers.CreateDb();

        Assert.True(db.Database.IsRelational());
    }
}
