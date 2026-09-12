using System.Data.Common;
using Escalated.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Escalated.Tests;

/// <summary>
/// The database the suite runs against.
///
/// <para>SQLite by default, so running the suite locally needs nothing
/// installed. Set <c>ESCALATED_TEST_DATABASE=postgres</c> — with
/// <c>ESCALATED_TEST_CONNECTION</c> for the connection string — and the same
/// tests run against a real PostgreSQL server.</para>
///
/// <para>An unrecognised value throws rather than falling back. A CI leg that
/// quietly ran SQLite would report green having tested nothing the matrix
/// exists for, and every assertion in the suite would still pass.</para>
/// </summary>
public static class TestDatabase
{
    private const string DefaultConnection =
        "Host=127.0.0.1;Port=5432;Database=escalated_test;Username=postgres;Password=postgres";

    private static int schemaCounter;

    /// <summary>What the suite was asked to run against: "sqlite" or "postgres".</summary>
    public static string Provider
    {
        get
        {
            var provider = Environment.GetEnvironmentVariable("ESCALATED_TEST_DATABASE");

            if (string.IsNullOrWhiteSpace(provider))
            {
                return "sqlite";
            }

            return provider switch
            {
                "sqlite" or "postgres" => provider,
                _ => throw new InvalidOperationException(
                    $"ESCALATED_TEST_DATABASE must be sqlite or postgres; got \"{provider}\"."),
            };
        }
    }

    /// <summary>Returns a context whose tables exist and are empty.</summary>
    public static EscalatedDbContext Create(string? name = null)
    {
        if (Provider == "sqlite")
        {
            var sqlite = CreateSqlite();
            sqlite.Database.EnsureCreated();

            return sqlite;
        }

        var postgres = CreatePostgres();

        // Not EnsureCreated: it asks whether the *database* exists, and on a
        // server it always does, so it would create nothing and leave every
        // test after the first querying an empty schema. CreateTables builds
        // the model's tables regardless.
        postgres.Database.GetService<IRelationalDatabaseCreator>().CreateTables();

        return postgres;
    }

    private static EscalatedDbContext CreateSqlite()
    {
        // The connection is kept open for the context's lifetime: an in-memory
        // SQLite database exists only while something holds a connection to it,
        // so closing between commands would discard the schema.
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        return new TrackedContext(
            new DbContextOptionsBuilder<EscalatedDbContext>().UseSqlite(connection).Options,
            connection);
    }

    private static EscalatedDbContext CreatePostgres()
    {
        var schema = $"esc_test_{Environment.ProcessId}_{Interlocked.Increment(ref schemaCounter)}";

        CreateSchema(schema);

        var builder = new DbContextOptionsBuilder<EscalatedDbContext>();

        // The schema is set on the connection rather than on the model. EF
        // caches the model per context type, so HasDefaultSchema would bake the
        // first test's schema into every context after it -- and they would all
        // create their tables on top of each other. A search path is per
        // connection, which is what the isolation actually needs.
        // Pooling off. Every test gets its own search path, so every test gets
        // its own connection string -- and Npgsql keeps a pool per connection
        // string. Several hundred of them exhausts the server's connection
        // limit long before the suite finishes ("sorry, too many clients
        // already"). Tests are short; a pool buys nothing here.
        builder.UseNpgsql($"{ConnectionString()};Search Path={schema};Pooling=false");

        return new SchemaContext(builder.Options, schema);
    }

    private static void CreateSchema(string schema)
    {
        using var connection = new NpgsqlConnection($"{ConnectionString()};Pooling=false");
        connection.Open();

        using var command = connection.CreateCommand();

        // The name is generated here from the process id and a counter, never
        // from input, and DDL takes no parameters.
        command.CommandText = $"CREATE SCHEMA IF NOT EXISTS \"{schema}\"";
        command.ExecuteNonQuery();
    }

    private static void DropSchema(string schema)
    {
        try
        {
            using var connection = new NpgsqlConnection($"{ConnectionString()};Pooling=false");
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = $"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE";
            command.ExecuteNonQuery();
        }
        catch (NpgsqlException)
        {
            // A run that lost the server has nothing to clean up, and failing
            // here would mask whatever actually went wrong.
        }
    }

    private static string ConnectionString()
    {
        var connection = Environment.GetEnvironmentVariable("ESCALATED_TEST_CONNECTION");

        return string.IsNullOrWhiteSpace(connection) ? DefaultConnection : connection;
    }

    /// <summary>Holds the SQLite connection open for as long as the context lives.</summary>
    private sealed class TrackedContext : EscalatedDbContext
    {
        private readonly DbConnection connection;

        public TrackedContext(DbContextOptions<EscalatedDbContext> options, DbConnection connection)
            : base(options)
        {
            this.connection = connection;
        }

        public override void Dispose()
        {
            base.Dispose();
            connection.Dispose();
        }
    }

    /// <summary>Drops the schema this context's tables were created in.</summary>
    private sealed class SchemaContext : EscalatedDbContext
    {
        private readonly string schema;

        public SchemaContext(DbContextOptions<EscalatedDbContext> options, string schema)
            : base(options)
        {
            this.schema = schema;
        }

        public override void Dispose()
        {
            base.Dispose();
            DropSchema(schema);
        }
    }
}
