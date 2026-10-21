using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Escalated.Data;

/// <summary>
/// Design-time context for <c>dotnet ef</c>, used to generate the package's own
/// migrations. The package ships one migration set per provider (see
/// <see cref="EscalatedMigrationsAssembly"/>), so the provider is an argument:
/// <code>
/// dotnet ef migrations add &lt;Name&gt; --project src/Escalated --startup-project src/Escalated \
///   --output-dir Migrations/Postgres --namespace Escalated.Migrations.Postgres -- postgres
/// </code>
/// Run it once for each of <c>sqlite</c>, <c>postgres</c> and <c>sqlserver</c> with the
/// same name. Each run rewrites one shared <c>EscalatedDbContextModelSnapshot.cs</c>, so
/// keep one snapshot per provider folder (copy it from that run's Designer file).
/// No server is contacted.
/// </summary>
public sealed class EscalatedDbContextFactory : IDesignTimeDbContextFactory<EscalatedDbContext>
{
    public EscalatedDbContext CreateDbContext(string[] args)
    {
        var provider = args.FirstOrDefault()?.ToLowerInvariant() ?? "sqlite";
        var builder = new DbContextOptionsBuilder<EscalatedDbContext>();
        switch (provider)
        {
            case "sqlite":
                builder.UseSqlite($"Data Source={Path.Combine(Path.GetTempPath(), "escalated-dotnet-ef.design.db")}");
                break;
            case "postgres":
                builder.UseNpgsql("Host=localhost;Database=escalated_design");
                break;
            case "sqlserver":
                builder.UseSqlServer("Server=localhost;Database=escalated_design;Trusted_Connection=True;TrustServerCertificate=True");
                break;
            default:
                throw new ArgumentException($"Unknown provider \"{provider}\"; expected sqlite, postgres or sqlserver.");
        }

        return new EscalatedDbContext(builder.Options);
    }
}
