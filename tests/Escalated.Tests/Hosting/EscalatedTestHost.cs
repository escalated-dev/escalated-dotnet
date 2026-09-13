using System.Security.Claims;
using System.Text.Encodings.Web;
using Escalated.Configuration;
using Escalated.Data;
using Escalated.Extensions;
using Escalated.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Escalated.Tests.Hosting;

/// <summary>
/// A real ASP.NET Core pipeline, wired the way <c>docker/host-app/Program.cs</c>
/// wires one: <c>AddEscalated()</c>, the package's controllers added with
/// <c>AddApplicationPart</c>, authentication and authorization middleware, and
/// <c>MapControllers()</c>.
///
/// <para>The controller tests elsewhere in the suite construct a controller and
/// call its action directly. That skips everything MVC does before an action
/// runs: discovering controllers, validating their routes, building the endpoint
/// table, authorizing the request, binding parameters, running filters. A
/// controller MVC refuses to load took every endpoint down with it, and none of
/// those tests could notice.</para>
///
/// <para>Requests are anonymous unless made through <see cref="ClientFor"/>, which
/// signs them in as a host user the way a host's own authentication would.</para>
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

    /// <summary>An anonymous client.</summary>
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

            // The host's authentication. Escalated owns none; it reads whoever the
            // host signed in from HttpContext.User.
            builder.Services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            builder.Services.AddAuthorization();

            RemoveEscalatedBackgroundServices(builder.Services);
            configureServices?.Invoke(builder.Services);

            var app = builder.Build();

            using (var scope = app.Services.CreateScope())
            {
                database.CreateTables(scope.ServiceProvider.GetRequiredService<EscalatedDbContext>());
            }

            app.UseAuthentication();
            app.UseAuthorization();
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

    /// <summary>A client whose requests are authenticated as <paramref name="userId"/>.</summary>
    public HttpClient ClientFor(string userId, params (string Type, string Value)[] claims)
    {
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId);

        foreach (var (type, value) in claims)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.ClaimHeader, $"{type}={value}");
        }

        return client;
    }

    public async Task SeedAsync(Func<EscalatedDbContext, Task> seed)
    {
        using var scope = app.Services.CreateScope();
        await seed(scope.ServiceProvider.GetRequiredService<EscalatedDbContext>());
    }

    public async Task<T> QueryAsync<T>(Func<EscalatedDbContext, Task<T>> query)
    {
        using var scope = app.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<EscalatedDbContext>());
    }

    /// <summary>
    /// Grants one of the roles the admin users page manages
    /// (<c>escalated-admin</c>, <c>escalated-agent</c>).
    /// </summary>
    public Task GrantRoleAsync(string userId, string slug) => SeedAsync(async db =>
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Slug == slug);
        if (role is null)
        {
            role = new Role { Name = slug, Slug = slug, IsSystem = true };
            db.Roles.Add(role);
            await db.SaveChangesAsync();
        }

        db.RoleUsers.Add(new RoleUser { RoleId = role.Id, UserId = userId });
        await db.SaveChangesAsync();
    });

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

/// <summary>
/// Stands in for a host's authentication: a request carrying
/// <see cref="UserHeader"/> is signed in as that user id, with a
/// <see cref="ClaimTypes.NameIdentifier"/> claim, the way ASP.NET Core Identity and
/// JWT bearer authentication both identify a user. Any <see cref="ClaimHeader"/>
/// values are added as <c>type=value</c> claims. Without the header the request is
/// anonymous.
/// </summary>
public sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Test";
    public const string UserHeader = "X-Test-User";
    public const string ClaimHeader = "X-Test-Claim";

    public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserHeader, out var user) || string.IsNullOrEmpty(user))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, user.ToString()) };

        foreach (var header in Request.Headers[ClaimHeader])
        {
            var parts = header!.Split('=', 2);
            claims.Add(new Claim(parts[0], parts.Length > 1 ? parts[1] : string.Empty));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
