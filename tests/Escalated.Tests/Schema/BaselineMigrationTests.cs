using Escalated.Data;
using Escalated.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Escalated.Tests.Schema;

/// <summary>
/// The package ships a full baseline per provider. Before it, the bundled migrations
/// only added deltas, so an empty database ended up with no Escalated tables.
/// </summary>
public class BaselineMigrationTests
{
    private static DbContextOptions<EscalatedDbContext> Sqlite(SqliteConnection connection) =>
        new DbContextOptionsBuilder<EscalatedDbContext>().UseSqlite(connection).Options;

    private static async Task<HashSet<string>> SqliteTablesAsync(SqliteConnection connection)
    {
        var tables = new HashSet<string>(StringComparer.Ordinal);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
        return tables;
    }

    [Fact]
    public async Task Migrate_OnAnEmptySqliteDatabase_CreatesEveryEntityTable()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new EscalatedDbContext(Sqlite(connection));

        await db.Database.MigrateAsync();

        var tables = await SqliteTablesAsync(connection);
        var expected = db.Model.GetEntityTypes().Select(e => e.GetTableName()).Where(t => t is not null).Distinct().ToList();
        Assert.NotEmpty(expected);
        Assert.All(expected, table => Assert.Contains(table!, tables));
        Assert.Contains("escalated_tickets", tables);
        Assert.Contains("escalated_department_agent", tables);

        db.Tickets.Add(new Ticket { Reference = "ESC-1", Subject = "Works", AccountType = "Company", AccountId = "ABC" });
        await db.SaveChangesAsync();
        Assert.Equal(1, await db.Tickets.IgnoreQueryFilters().CountAsync());
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("postgres")]
    [InlineData("sqlserver")]
    public void EachProvider_HasAMigrationSetThatMatchesTheModel(string provider)
    {
        var builder = new DbContextOptionsBuilder<EscalatedDbContext>();
        switch (provider)
        {
            case "sqlite": builder.UseSqlite("Data Source=:memory:"); break;
            case "postgres": builder.UseNpgsql("Host=localhost;Database=unused"); break;
            default: builder.UseSqlServer("Server=localhost;Database=unused;Trusted_Connection=True"); break;
        }
        using var db = new EscalatedDbContext(builder.Options);

        Assert.NotEmpty(db.GetService<IMigrationsAssembly>().Migrations);
        Assert.False(db.Database.HasPendingModelChanges(), $"{provider}: the model has changes no migration captures");
    }

    [Fact]
    public void SqlServerBaseline_KeepsBinaryAccountColumnsAndTheVisitorTokenIndex()
    {
        var options = new DbContextOptionsBuilder<EscalatedDbContext>()
            .UseSqlServer("Server=localhost;Database=unused;Trusted_Connection=True").Options;
        using var db = new EscalatedDbContext(options);

        var sql = db.GetService<IMigrator>().GenerateScript();

        Assert.Contains("[AccountType] nvarchar(191) COLLATE Latin1_General_100_BIN2 NULL", sql);
        Assert.Contains("[VisitorTokenHash] nvarchar(64) NULL", sql);
        Assert.Contains("IX_escalated_chat_sessions_VisitorTokenHash", sql);
    }

    [Fact]
    public async Task MigrateAsync_AdoptsADatabaseCreatedWithEnsureCreated()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using (var legacy = new EscalatedDbContext(Sqlite(connection)))
        {
            await legacy.Database.EnsureCreatedAsync();
            legacy.Tickets.Add(new Ticket { Reference = "OLD-1", Subject = "Before upgrade" });
            await legacy.SaveChangesAsync();
        }

        await using var db = new EscalatedDbContext(Sqlite(connection));
        await EscalatedDatabase.MigrateAsync(db);

        Assert.NotEmpty(await db.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal("Before upgrade", (await db.Tickets.IgnoreQueryFilters().SingleAsync()).Subject);
    }

    [Fact]
    public async Task MigrateAsync_OnAnEmptyDatabase_RunsTheBaseline()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new EscalatedDbContext(Sqlite(connection));

        await EscalatedDatabase.MigrateAsync(db);

        Assert.Contains("escalated_tickets", await SqliteTablesAsync(connection));
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }
}
