using Escalated.Events;
using Escalated.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Escalated.Extensions;

/// <summary>
/// Adds the workflow stack (runner + dispatcher decorator) on top of
/// a service collection that already has <c>AddEscalated()</c> applied.
///
/// Host apps wire this in once during <c>Program.cs</c>:
/// <code>
/// services.AddEscalated().AddEscalatedWorkflows();
/// </code>
/// </summary>
public static class EscalatedWorkflowsServiceCollectionExtensions
{
    public static IServiceCollection AddEscalatedWorkflows(this IServiceCollection services)
    {
        services.TryAddScoped<WorkflowExecutorService>();
        services.TryAddScoped<WorkflowRunnerService>();

        // Decorate the existing IEscalatedEventDispatcher: every event
        // still reaches whichever dispatcher the host registered (or
        // NullEventDispatcher by default), and workflow-relevant events
        // additionally fire WorkflowRunnerService.
        var existing = services.FirstOrDefault(d => d.ServiceType == typeof(IEscalatedEventDispatcher));
        if (existing == null)
        {
            services.AddSingleton<IEscalatedEventDispatcher, NullEventDispatcher>();
        }

        // Keep a reference to the host's dispatcher via a keyed-style
        // factory. Since DI doesn't expose keyed registrations on all
        // netstandard targets, register a factory that materializes the
        // decorator.
        services.Replace(ServiceDescriptor.Scoped<IEscalatedEventDispatcher>(sp =>
        {
            // Pull the originally-registered impl type directly. If
            // AddEscalatedWorkflows is called twice we'd otherwise
            // double-wrap, so the check above is important.
            var innerType = existing?.ImplementationType ?? typeof(NullEventDispatcher);
            var inner = (IEscalatedEventDispatcher)ActivatorUtilities.CreateInstance(sp, innerType);
            var runner = sp.GetRequiredService<WorkflowRunnerService>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<WorkflowEventDispatcher>>();
            return new WorkflowEventDispatcher(inner, runner, logger);
        }));

        return services;
    }
}
