using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Escalated.Data;

/// <summary>
/// Creates or upgrades Escalated's tables with the migrations shipped in the package.
/// </summary>
public static class EscalatedDatabase
{
    /// <summary>
    /// Applies Escalated's migrations to the context's database.
    ///
    /// <para>Before 0.2.2 the package shipped only partial migrations, so working
    /// installs created their tables with <c>EnsureCreated()</c> and have no
    /// migrations history. When the Escalated tables already exist but no Escalated
    /// migration is recorded, the bundled baseline is recorded as applied instead
    /// of run, so later migrations apply on top of the existing tables. Tables
    /// added to the model after such a database was created are not back-filled;
    /// see the upgrade notes in the CHANGELOG.</para>
    /// </summary>
    public static async Task MigrateAsync(EscalatedDbContext db, CancellationToken ct = default)
    {
        var migrations = db.GetService<IMigrationsAssembly>().Migrations.Keys.OrderBy(id => id, StringComparer.Ordinal).ToList();
        if (migrations.Count > 0
            && !(await db.Database.GetAppliedMigrationsAsync(ct)).Any()
            && await TicketsTableExistsAsync(db, ct))
        {
            var history = db.GetService<IHistoryRepository>();
            await db.Database.ExecuteSqlRawAsync(history.GetCreateIfNotExistsScript(), ct);
            await db.Database.ExecuteSqlRawAsync(
                history.GetInsertScript(new HistoryRow(migrations[0], ProductInfo.GetVersion())), ct);
        }

        await db.Database.MigrateAsync(ct);
    }

    /// <summary>
    /// Resolves an <see cref="EscalatedDbContext"/> from a new scope and runs
    /// <see cref="MigrateAsync(EscalatedDbContext, CancellationToken)"/>. Call once at startup.
    /// </summary>
    public static async Task MigrateEscalatedDatabaseAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        await MigrateAsync(scope.ServiceProvider.GetRequiredService<EscalatedDbContext>(), ct);
    }

    private static async Task<bool> TicketsTableExistsAsync(EscalatedDbContext db, CancellationToken ct)
    {
        try
        {
            await db.Tickets.IgnoreQueryFilters().AnyAsync(ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }
}
