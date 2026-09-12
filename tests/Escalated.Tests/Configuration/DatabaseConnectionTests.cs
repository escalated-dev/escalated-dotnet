using System.Reflection;
using Escalated.Data;
using Escalated.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Escalated.Tests.Configuration;

/// <summary>
/// Escalated's tables live on whatever connection <c>EscalatedDbContext</c> is
/// given, which need not be the host application's database — it can be a
/// schema shared with a legacy system, a separate reporting store, or simply a
/// database the host would rather not mix support data into.
///
/// The separation only holds while nothing in the package reads a table the
/// package does not own. On a separate database such a read does not throw; it
/// returns nothing, which reads as users who do not exist — a failure that
/// survives a whole test suite and surfaces as an empty admin panel.
/// </summary>
public class DatabaseConnectionTests
{
    /// <summary>Tables the host owns. Escalated stores user ids as plain
    /// unconstrained values precisely so these can live somewhere else.</summary>
    private static readonly string[] HostOwnedTables =
    {
        "users", "aspnetusers", "aspnetroles", "aspnetuserroles", "accounts", "members"
    };

    private static IModel BuildModel()
    {
        var options = new DbContextOptionsBuilder<EscalatedDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        using var context = new EscalatedDbContext(options);

        return context.Model;
    }

    [Fact]
    public void NoEntityIsMappedToAHostOwnedTable()
    {
        var offenders = BuildModel()
            .GetEntityTypes()
            .Select(entity => entity.GetTableName())
            .Where(table => table is not null)
            .Where(table => HostOwnedTables.Contains(table!.ToLowerInvariant()))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "EscalatedDbContext maps entities to host-owned tables: " + string.Join(", ", offenders) +
            ". Those tables may not exist on Escalated's connection at all; reach host user data " +
            "through IUserDirectory instead.");
    }

    [Fact]
    public void NoEntityDeclaresAForeignKeyToAHostOwnedTable()
    {
        // A foreign key would force both tables onto one database, which is
        // exactly what the split has to avoid. Ticket.RequesterId and friends
        // are deliberately plain columns.
        var offenders = BuildModel()
            .GetEntityTypes()
            .SelectMany(entity => entity.GetForeignKeys())
            .Select(fk => fk.PrincipalEntityType.GetTableName())
            .Where(table => table is not null)
            .Where(table => HostOwnedTables.Contains(table!.ToLowerInvariant()))
            .Distinct()
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Escalated entities declare foreign keys to host-owned tables: " + string.Join(", ", offenders));
    }

    [Fact]
    public void NoRawSqlInThePackageNamesAHostOwnedTable()
    {
        // Raw SQL bypasses the model, so the two checks above cannot see it.
        var assembly = typeof(EscalatedDbContext).Assembly;
        var offenders = new List<string>();

        foreach (var name in assembly.GetManifestResourceNames())
        {
            using var stream = assembly.GetManifestResourceStream(name);
            if (stream is null)
            {
                continue;
            }

            using var reader = new StreamReader(stream);
            var body = reader.ReadToEnd().ToLowerInvariant();

            foreach (var table in HostOwnedTables)
            {
                if (body.Contains("from " + table) || body.Contains("join " + table))
                {
                    offenders.Add($"{name} ({table})");
                }
            }
        }

        Assert.True(offenders.Count == 0, "raw SQL names host-owned tables: " + string.Join(", ", offenders));
    }

    [Fact]
    public async Task TheDefaultUserDirectoryReadsNoDatabaseAtAll()
    {
        // A host that has not wired its users up gets an empty admin page
        // rather than a query against whatever Escalated's connection points
        // at — which on a split install is a database with no users in it.
        var directory = new NullUserDirectory();

        var page = await directory.ListAsync(search: null, page: 1, pageSize: 20);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.Total);
        Assert.Null(await directory.FindAsync("1"));
    }

    [Fact]
    public void UserDirectoryIsTheOnlySeamForHostUsers()
    {
        // Pins the contract: reads of host users go through this interface, so
        // they run on the host's own connection with the host's own query.
        var methods = typeof(IUserDirectory)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.Name)
            .OrderBy(n => n)
            .ToArray();

        Assert.Equal(new[] { "FindAsync", "ListAsync" }, methods);
    }
}
