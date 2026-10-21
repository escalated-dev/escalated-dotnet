using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Internal;

namespace Escalated.Data;

/// <summary>
/// Serves the package's migrations for the provider the context is running on.
///
/// <para>A migration and its model snapshot carry provider-specific store types,
/// identity strategies and collations, so one set cannot be correct for SQLite,
/// PostgreSQL and SQL Server at once. The package therefore ships one set per
/// provider, each in its own namespace under <c>Escalated.Migrations</c>, and this
/// picks the set matching <see cref="DatabaseFacade.ProviderName"/>.</para>
///
/// <para>A host that keeps Escalated's migrations in its own assembly
/// (<c>MigrationsAssembly("MyApp")</c>) gets EF's default behaviour unchanged.</para>
/// </summary>
#pragma warning disable EF1001 // MigrationsAssembly is the documented extension point for this.
public sealed class EscalatedMigrationsAssembly : MigrationsAssembly
{
    /// <summary>Provider name to migration namespace.</summary>
    public static readonly IReadOnlyDictionary<string, string> ProviderNamespaces = new Dictionary<string, string>
    {
        ["Microsoft.EntityFrameworkCore.Sqlite"] = "Escalated.Migrations.Sqlite",
        ["Npgsql.EntityFrameworkCore.PostgreSQL"] = "Escalated.Migrations.Postgres",
        ["Microsoft.EntityFrameworkCore.SqlServer"] = "Escalated.Migrations.SqlServer",
    };

    private readonly Type contextType;
    private readonly string? providerNamespace;
    private IReadOnlyDictionary<string, TypeInfo>? migrations;
    private ModelSnapshot? modelSnapshot;
    private bool snapshotResolved;

    public EscalatedMigrationsAssembly(
        ICurrentDbContext currentContext,
        IDbContextOptions options,
        IMigrationsIdGenerator idGenerator,
        IDiagnosticsLogger<DbLoggerCategory.Migrations> logger)
        : base(currentContext, options, idGenerator, logger)
    {
        contextType = currentContext.Context.GetType();
        var provider = currentContext.Context.Database.ProviderName;
        providerNamespace = provider is not null && ProviderNamespaces.TryGetValue(provider, out var ns) ? ns : null;
    }

    private bool ServesPackageMigrations => Assembly == typeof(EscalatedDbContext).Assembly;

    public override IReadOnlyDictionary<string, TypeInfo> Migrations
    {
        get
        {
            if (!ServesPackageMigrations) return base.Migrations;

            return migrations ??= Assembly.DefinedTypes.Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false })
                .Where(t => t.IsSubclassOf(typeof(Migration)) && InProviderNamespace(t) && TargetsThisContext(t))
                .Select(t => (Id: t.GetCustomAttribute<MigrationAttribute>()?.Id, Type: t))
                .Where(m => m.Id is not null)
                .OrderBy(m => m.Id, StringComparer.Ordinal)
                .ToDictionary(m => m.Id!, m => m.Type);
        }
    }

    public override ModelSnapshot? ModelSnapshot
    {
        get
        {
            if (!ServesPackageMigrations) return base.ModelSnapshot;
            if (snapshotResolved) return modelSnapshot;

            var type = Assembly.DefinedTypes.Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false })
                .FirstOrDefault(t => t.IsSubclassOf(typeof(ModelSnapshot)) && InProviderNamespace(t) && TargetsThisContext(t));
            modelSnapshot = type is null ? null : (ModelSnapshot)Activator.CreateInstance(type.AsType())!;
            snapshotResolved = true;
            return modelSnapshot;
        }
    }

    private bool InProviderNamespace(TypeInfo type) => providerNamespace is not null && type.Namespace == providerNamespace;

    // Subclasses of EscalatedDbContext (a host's, or a test's) use the same schema.
    private bool TargetsThisContext(TypeInfo type) =>
        type.GetCustomAttribute<DbContextAttribute>()?.ContextType is { } target && target.IsAssignableFrom(contextType);
}
#pragma warning restore EF1001
