using Escalated.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Escalated.Data;

/// <summary>Design-time provider for dotnet-ef scaffolding (skills parity migration).</summary>
public sealed class EscalatedDbContextFactory : IDesignTimeDbContextFactory<EscalatedDbContext>
{
    public EscalatedDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<EscalatedDbContext>()
            .UseSqlite($"Data Source={Path.Combine(Path.GetTempPath(), "escalated-dotnet-ef.design.db")}")
            .Options;

        return new EscalatedDbContext(options);
    }
}
