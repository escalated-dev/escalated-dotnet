using System.Text.Json.Nodes;
using System.Threading.Channels;
using Escalated.Data;
using Escalated.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Escalated.Plugins;

/// <summary>
/// Delivers actions to active plugins in the background, one at a time and in the
/// order they were raised, each from a fresh scope acting as nobody. A slow or
/// failing plugin never fails or delays the request that raised the action.
///
/// <para>What plugins do in response (an event their <c>ctx</c> writes cause, or a
/// <c>ctx.emit</c>) is delivered too, at most <see cref="MaxDepth"/> levels deep, so
/// two plugins answering each other cannot loop forever.</para>
/// </summary>
public sealed class PluginEventRelay
{
    public const int MaxDepth = 5;

    private static readonly AsyncLocal<int> Depth = new();

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<PluginEventRelay> _logger;
    private readonly Channel<(string Hook, JsonNode? Payload, int Depth)> _queue =
        Channel.CreateUnbounded<(string, JsonNode?, int)>(new UnboundedChannelOptions { SingleReader = true });
    private readonly object _sync = new();
    private Task? _worker;
    private int _pending;
    private TaskCompletionSource _idle = Completed();

    public PluginEventRelay(IServiceScopeFactory scopes, ILogger<PluginEventRelay> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    /// <summary>Queues <paramref name="hook"/> for every active plugin that handles it.</summary>
    public void Enqueue(string hook, JsonNode? payload)
    {
        var depth = Depth.Value + 1;
        if (depth > MaxDepth)
        {
            _logger.LogWarning("[Escalated plugins] {Hook}, caused by plugins {Depth} levels deep, was not delivered.", hook, depth - 1);
            return;
        }

        lock (_sync)
        {
            if (_pending++ == 0) _idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Writer.TryWrite((hook, payload, depth));

            if (_worker is null)
            {
                // Not the caller's ambient state: no HttpContext, ticket visibility
                // scope or bridge call carries over into delivery.
                using (ExecutionContext.SuppressFlow())
                {
                    _worker = Task.Run(RunAsync);
                }
            }
        }
    }

    /// <summary>Completes when everything queued so far (and what it caused) is delivered.</summary>
    public Task WhenIdleAsync()
    {
        lock (_sync) return _idle.Task;
    }

    private async Task RunAsync()
    {
        await foreach (var (hook, payload, depth) in _queue.Reader.ReadAllAsync())
        {
            try
            {
                Depth.Value = depth;
                await using var scope = _scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<PluginRegistry>().DispatchActionAsync(hook, payload, PluginActor.None);
            }
            catch (Exception e)
            {
                _logger.LogWarning("[Escalated plugins] {Hook} was not delivered: {Error}", hook, e.Message);
            }
            finally
            {
                Depth.Value = 0;
                lock (_sync)
                {
                    if (--_pending == 0) _idle.TrySetResult();
                }
            }
        }
    }

    private static TaskCompletionSource Completed()
    {
        var done = new TaskCompletionSource();
        done.SetResult();
        return done;
    }
}

/// <summary>
/// Hands the canonical events (ADR 2026-10-21) to <see cref="PluginEventRelay"/>,
/// under exactly those names, with the payloads of the plugin contract. Runs in
/// <see cref="EscalatedEventDispatcher"/> after the change is saved; legacy names
/// are never delivered. Nothing is built while no plugin is active.
/// </summary>
public sealed class PluginEventDispatcher : IEscalatedEventDispatcher
{
    private readonly EscalatedDbContext _db;
    private readonly PluginPayloads _payloads;
    private readonly PluginEventRelay _relay;
    private readonly ILogger<PluginEventDispatcher> _logger;

    public PluginEventDispatcher(EscalatedDbContext db, PluginPayloads payloads, PluginEventRelay relay, ILogger<PluginEventDispatcher> logger)
    {
        _db = db;
        _payloads = payloads;
        _relay = relay;
        _logger = logger;
    }

    public async Task DispatchAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : class
    {
        if (WebhookEventDispatcher.EventName(@event) is null) return;

        try
        {
            if (!await _db.Plugins.AsNoTracking().AnyAsync(p => p.IsActive, ct)) return;
            if (await _payloads.ForEventAsync(@event, ct) is { } built)
            {
                _relay.Enqueue(built.Name, built.Payload);
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _logger.LogWarning("[Escalated plugins] could not queue {Event} for plugins: {Error}", typeof(TEvent).Name, e.Message);
        }
    }
}
