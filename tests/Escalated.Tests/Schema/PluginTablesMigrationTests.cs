using Escalated.Data;
using Escalated.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Escalated.Tests.Schema;

/// <summary>
/// The plugin contract's tables, <c>escalated_plugins</c> and
/// <c>escalated_plugin_store</c>, in every provider's migrations.
/// </summary>
public class PluginTablesMigrationTests
{
    private static string Script(string provider)
    {
        var builder = new DbContextOptionsBuilder<EscalatedDbContext>();
        switch (provider)
        {
            case "sqlite": builder.UseSqlite("Data Source=:memory:"); break;
            case "postgres": builder.UseNpgsql("Host=localhost;Database=unused"); break;
            default: builder.UseSqlServer("Server=localhost;Database=unused;Trusted_Connection=True"); break;
        }

        using var db = new EscalatedDbContext(builder.Options);
        return db.GetService<IMigrator>().GenerateScript();
    }

    [Fact]
    public void PostgresStoresPluginDataAsJsonbWithTheContractsLengths()
    {
        var sql = Script("postgres");

        Assert.Contains("CREATE TABLE escalated_plugin_store", sql);
        Assert.Contains("\"Data\" jsonb", sql);
        Assert.Contains("\"Plugin\" character varying(191) NOT NULL", sql);
        Assert.Contains("\"Collection\" character varying(100) NOT NULL", sql);
        Assert.Contains("\"Key\" character varying(191)", sql);
        Assert.Contains("ALTER TABLE escalated_plugins ALTER COLUMN \"Slug\" TYPE character varying(255)", sql);
        Assert.Contains("IX_escalated_plugin_store_Plugin_Collection_Key", sql);
    }

    [Fact]
    public void SqlServerWidensTheSlugAndCreatesTheStore()
    {
        var sql = Script("sqlserver");

        Assert.Contains("CREATE TABLE [escalated_plugin_store]", sql);
        Assert.Contains("[Data] nvarchar(max) NULL", sql);
        Assert.Contains("[Slug] nvarchar(255) NOT NULL", sql);
        Assert.Contains("IX_escalated_plugins_Slug", sql);
    }

    [Fact]
    public async Task SqliteMigratesAndRoundTripsAStoreRow()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new EscalatedDbContext(new DbContextOptionsBuilder<EscalatedDbContext>().UseSqlite(connection).Options);

        await EscalatedDatabase.MigrateAsync(db);
        db.Plugins.Add(new Plugin { Slug = new string('a', 255), IsActive = true });
        db.PluginStore.Add(new PluginStoreRecord { Plugin = "@acme/crm", Collection = "__config__", Key = "__config__", Data = "{\"a\":1}" });
        await db.SaveChangesAsync();

        Assert.Equal("{\"a\":1}", (await db.PluginStore.SingleAsync()).Data);
        Assert.True((await db.Plugins.SingleAsync()).IsActive);
    }
}
