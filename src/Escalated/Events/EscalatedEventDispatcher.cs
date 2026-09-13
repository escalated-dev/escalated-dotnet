using Microsoft.Extensions.DependencyInjection;

namespace Escalated.Events;

/// <summary>
/// The event bus every Escalated service dispatches domain events through.
///
/// <para>Each event goes, in order, to:</para>
/// <list type="number">
///   <item><see cref="WebhookEventDispatcher"/>, which delivers it to subscribed outbound webhooks;</item>
///   <item><see cref="WorkflowEventDispatcher"/>, which runs matching Workflows;</item>
///   <item>every <see cref="IEscalatedEventDispatcher"/> the host registered, in registration order.</item>
/// </list>
///
/// <para>Webhooks go first, as the Laravel reference registers them: a Workflow
/// action raises events of its own, and a subscriber should see
/// <c>ticket.created</c> before the <c>ticket.priority_changed</c> a Workflow
/// made in response to it.</para>
///
/// <para>Host dispatchers are additive. Registering one, before or after
/// <c>AddEscalated</c>, adds a listener; it no longer replaces the Workflow bridge,
/// which it used to do silently. Escalated's services are built with this class
/// rather than whatever <see cref="IEscalatedEventDispatcher"/> resolves to last.</para>
///
/// <para>Scoped, so a host dispatcher may itself be scoped (for example one that
/// writes through the host's own <c>DbContext</c>).</para>
/// </summary>
public sealed class EscalatedEventDispatcher : IEscalatedEventDispatcher
{
    private readonly WebhookEventDispatcher _webhooks;
    private readonly WorkflowEventDispatcher _workflows;
    private readonly IServiceProvider _services;

    public EscalatedEventDispatcher(
        WebhookEventDispatcher webhooks,
        WorkflowEventDispatcher workflows,
        IServiceProvider services)
    {
        _webhooks = webhooks;
        _workflows = workflows;
        _services = services;
    }

    public async Task DispatchAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : class
    {
        await _webhooks.DispatchAsync(@event, ct);
        await _workflows.DispatchAsync(@event, ct);

        foreach (var listener in HostDispatchers())
        {
            await listener.DispatchAsync(@event, ct);
        }
    }

    /// <summary>
    /// Everything registered as <see cref="IEscalatedEventDispatcher"/> except
    /// Escalated's own: this bus (which is registered under the interface too) and
    /// the two internal dispatchers, in case a host registered one of them
    /// explicitly, as the previous defaults invited, and would otherwise run it twice.
    /// </summary>
    private IEnumerable<IEscalatedEventDispatcher> HostDispatchers() =>
        _services.GetServices<IEscalatedEventDispatcher>()
            .Where(d => d is not (EscalatedEventDispatcher or WorkflowEventDispatcher or WebhookEventDispatcher));
}
