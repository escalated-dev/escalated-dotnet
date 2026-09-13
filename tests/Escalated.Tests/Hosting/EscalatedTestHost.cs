using Escalated.Configuration;
using Escalated.Data;
using Escalated.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Escalated.Tests.Hosting;

/// <summary>
/// A real ASP.NET Core pipeline, wired the way <c>docker/host-app/Program.cs</c>
/// wires one: <c>AddEscalated()</c>, the package's controllers added with
/// <c>AddApplicationPart</c>, and <c>MapControllers()</c>.
///
/// <para>The controller tests elsewhere in the suite construct a controller and
/// call its action directly. That skips everything MVC does before an action
/// runs: discovering controllers, validating their routes, building the endpoint
/// table, binding parameters, running filters. A controller MVC refuses to load
/// took every endpoint down with it, and none of those tests could notice.</para>
/// </summary>
public sealed class EscalatedTestHost : IAsyncDisposable
{
    private readonly WebApplication app;
    private readonly TestDatabase.Lease database;

    private EscalatedTestHost(WebApplication app, TestDatabase.Lease database)
    {
        this.app = app;
        this.database = database;
        Client = app.GetTestClient();
    }

    public HttpClient Client { get; }

    public IServiceProvider Services => app.Services;

    /// <summary>Every routed endpoint MVC built, for tests that inspect the route table.</summary>
    public IEnumerable<RouteEndpoint> Endpoints =>
        app.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>();

    public static async Task<EscalatedTestHost> StartAsync(
        Action<EscalatedOptions>? configureOptions = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var database = TestDatabase.LeaseDatabase();

        try
        {
            // Not Development: that environment adds the developer exception page,
            // which turns an unhandled exception into a 500 page instead of letting
            // it reach the test.
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();

            builder.Services.AddDbContext<EscalatedDbContext>(database.Configure);
            builder.Services.AddEscalated();

            if (configureOptions is not null)
            {
                builder.Services.Configure(configureOptions);
            }

            builder.Services.AddControllers()
                .AddApplicationPart(typeof(EscalatedDbContext).Assembly);

            RemoveEscalatedBackgroundServices(builder.Services);
            configureServices?.Invoke(builder.Services);

            var app = builder.Build();

            using (var scope = app.Services.CreateScope())
            {
                database.CreateTables(scope.ServiceProvider.GetRequiredService<EscalatedDbContext>());
            }

            app.MapControllers();
            await app.StartAsync();

            return new EscalatedTestHost(app, database);
        }
        catch
        {
            database.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The schedulers poll the database on timers. Left running they would write
    /// to it while a test does, so a test would depend on when they last woke.
    /// </summary>
    private static void RemoveEscalatedBackgroundServices(IServiceCollection services)
    {
        var escalated = typeof(EscalatedDbContext).Assembly;

        foreach (var descriptor in services
                     .Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType?.Assembly == escalated)
                     .ToList())
        {
            services.Remove(descriptor);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await app.StopAsync();
        await app.DisposeAsync();
        database.Dispose();
    }
}
