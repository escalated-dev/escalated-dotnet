using Escalated.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Escalated.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all Escalated services in the DI container so the
    /// library's controllers (added via AddApplicationPart) can resolve
    /// their constructor dependencies.
    ///
    /// Registers exactly what <c>AddEscalated(configuration, configureDb)</c>
    /// does, except that the host registers <c>EscalatedDbContext</c> and binds
    /// <c>EscalatedOptions</c> itself.
    /// </summary>
    public static IServiceCollection AddEscalated(this IServiceCollection services)
    {
        return services.AddEscalatedServices();
    }
}
