using Escalated.Data;
using Escalated.Migrations;
using Escalated.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Escalated.Tests.Models;

public class TicketAccessMigrationTests
{
    [Fact]
    public async Task UpgradeAndRollback_PreserveExistingTickets()
    {
        using var lease = TestDatabase.LeaseDatabase();
        var builder = new DbContextOptionsBuilder<EscalatedDbContext>();
        lease.Configure(builder);
        await using var legacy = new LegacyContext(builder.Options);
        if (TestDatabase.Provider == "sqlite") await legacy.Database.EnsureCreatedAsync();
        // PostgreSQL leases use an existing database, so create tables in their isolated schema.
        if (TestDatabase.Provider == "postgres")
            await legacy.Database.GetService<Microsoft.EntityFrameworkCore.Storage.IRelationalDatabaseCreator>().CreateTablesAsync();
        legacy.Tickets.Add(new Ticket { Reference = "OLD-1", Subject = "Before upgrade" });
        await legacy.SaveChangesAsync();
        await using var current = new EscalatedDbContext(builder.Options);
        var migration = new TicketAccessAndRouting { ActiveProvider = current.Database.ProviderName! };
        var generator = current.GetService<IMigrationsSqlGenerator>();
        var target = current.GetService<IDesignTimeModel>().Model;
        foreach (var command in generator.Generate(migration.UpOperations, target))
            await current.Database.ExecuteSqlRawAsync(command.CommandText);
        var ticket = await current.Tickets.SingleAsync();
        Assert.Null(ticket.AccountId);
        Assert.Null(ticket.AccountType);
        ticket.AccountType = "Company";
        ticket.AccountId = "ABC";
        await current.SaveChangesAsync();
        Assert.Empty(await current.Tickets.Where(t => t.AccountType == "Company" && t.AccountId == "abc").ToListAsync());
        foreach (var command in generator.Generate(migration.DownOperations, legacy.GetService<IDesignTimeModel>().Model))
            await legacy.Database.ExecuteSqlRawAsync(command.CommandText);
        legacy.ChangeTracker.Clear();
        Assert.Equal("Before upgrade", (await legacy.Tickets.SingleAsync()).Subject);
    }

    [Fact]
    public void SqlServerUpgrade_UsesBinaryAccountColumnsAndNullableTargets()
    {
        var options = new DbContextOptionsBuilder<EscalatedDbContext>().UseSqlServer("Server=localhost;Database=unused;Trusted_Connection=True").Options;
        using var db = new EscalatedDbContext(options);
        var migration = new TicketAccessAndRouting { ActiveProvider = db.Database.ProviderName! };
        var sql = string.Join("\n", db.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations, db.GetService<IDesignTimeModel>().Model).Select(command => command.CommandText));
        Assert.Contains("[AccountType] nvarchar(191) COLLATE Latin1_General_100_BIN2 NULL", sql);
        Assert.Contains("[AccountId] nvarchar(191) COLLATE Latin1_General_100_BIN2 NULL", sql);
        Assert.Contains("[DepartmentId] int NULL", sql);
        Assert.Contains("[escalated_department_agent]", sql);
    }

    private sealed class LegacyContext : EscalatedDbContext
    {
        public LegacyContext(DbContextOptions<EscalatedDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            foreach (var entity in modelBuilder.Model.GetEntityTypes()) entity.SetQueryFilter(null);
            modelBuilder.Ignore<DepartmentAgent>();
            modelBuilder.Entity<Ticket>().Ignore(t => t.AccountId).Ignore(t => t.AccountType);
            var policy = modelBuilder.Entity<SlaPolicy>().Metadata;
            foreach (var foreignKey in policy.GetForeignKeys().Where(key => key.Properties.Any(property => property.Name == "DepartmentId")).ToList())
                policy.RemoveForeignKey(foreignKey);
            modelBuilder.Entity<SlaPolicy>().Ignore(t => t.DepartmentId).Ignore(t => t.TicketType);
        }
    }
}
