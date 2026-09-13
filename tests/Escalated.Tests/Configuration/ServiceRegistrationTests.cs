using Escalated.Data;
using Escalated.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ConfiguredRegistration = Escalated.Configuration.EscalatedServiceCollectionExtensions;
using ParameterlessRegistration = Escalated.Extensions.ServiceCollectionExtensions;

namespace Escalated.Tests.Configuration;

/// <summary>
/// There are two ways to register Escalated: <c>AddEscalated(configuration, configureDb)</c>,
/// which the README documents, and the parameterless <c>AddEscalated()</c> the demo host
/// calls. Each kept its own list of registrations, and the lists drifted apart.
///
/// Nothing fails at startup when one of them misses a service. A controller is only built
/// when a request reaches it, so the gap is a 500 on that one endpoint. TicketService takes
/// MentionService as an optional argument, so there the gap is a feature that quietly does
/// nothing.
/// </summary>
public class ServiceRegistrationTests
{
    public static IEnumerable<object[]> Registrations() => new[]
    {
        new object[] { "configured" },
        new object[] { "parameterless" },
    };

    private static ServiceProvider Build(string registration, bool validate = false)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        if (registration == "configured")
        {
            var configuration = new ConfigurationBuilder().Build();
            ConfiguredRegistration.AddEscalated(services, configuration, o => o.UseSqlite("DataSource=:memory:"));
        }
        else
        {
            services.AddDbContext<EscalatedDbContext>(o => o.UseSqlite("DataSource=:memory:"));
            ParameterlessRegistration.AddEscalated(services);
        }

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = validate,
            ValidateScopes = validate,
        });
    }

    private static IEnumerable<Type> Controllers() =>
        typeof(EscalatedDbContext).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ControllerBase).IsAssignableFrom(t))
            .OrderBy(t => t.FullName);

    [Theory]
    [MemberData(nameof(Registrations))]
    public void EveryControllerCanBeConstructed(string registration)
    {
        using var provider = Build(registration);
        using var scope = provider.CreateScope();

        var failures = new List<string>();
        foreach (var controller in Controllers())
        {
            try
            {
                ActivatorUtilities.CreateInstance(scope.ServiceProvider, controller);
            }
            catch (InvalidOperationException e)
            {
                failures.Add($"{controller.Name}: {e.Message}");
            }
        }

        Assert.True(
            failures.Count == 0,
            $"The {registration} AddEscalated leaves these controllers unbuildable:\n" + string.Join("\n", failures));
    }

    [Theory]
    [MemberData(nameof(Registrations))]
    public void EveryRegisteredServiceCanBeResolved(string registration)
    {
        // ValidateOnBuild walks each registration's constructor graph up front. It catches
        // a service that is registered but depends on something that is not, which no
        // controller constructor would reveal.
        var exception = Record.Exception(() => Build(registration, validate: true).Dispose());

        Assert.True(exception is null, $"The {registration} AddEscalated registers unresolvable services:\n{exception?.Message}");
    }

    [Theory]
    [MemberData(nameof(Registrations))]
    public void MentionServiceIsRegistered(string registration)
    {
        using var provider = Build(registration);
        using var scope = provider.CreateScope();

        // TicketService is still constructed without it, with null, and @-mentions in
        // internal notes are then dropped without an error.
        Assert.NotNull(scope.ServiceProvider.GetService<MentionService>());
    }
}
