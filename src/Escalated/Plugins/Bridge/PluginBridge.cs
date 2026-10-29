using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Escalated.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Escalated.Plugins.Bridge;

/// <summary>
/// The plugin runtime could not be reached, did not answer in time, or answered
/// with an error. <see cref="PluginError"/> is the last: the runtime is fine, the
/// plugin's handler threw.
/// </summary>
public sealed class PluginBridgeException : Exception
{
    public PluginBridgeException(string message, bool pluginError = false, bool unavailable = false)
        : base(message)
    {
        PluginError = pluginError;
        Unavailable = unavailable;
    }

    public bool PluginError { get; }
    public bool Unavailable { get; }

    /// <summary>The runtime did not answer in time.</summary>
    public bool TimedOut { get; init; }
}

/// <summary>Answers the runtime's <c>ctx.*</c> requests for one dispatch.</summary>
public delegate Task<JsonNode?> PluginCtxHandler(string method, JsonObject parameters, CancellationToken ct);

/// <summary>
/// The SDK bridge: spawns <c>@escalated-dev/plugin-runtime</c> on first use and
/// speaks JSON-RPC 2.0 to it over stdio, one message per line (the plugin
/// contract's "SDK bridge").
///
/// <para>Off unless <see cref="PluginRuntimeOptions.Enabled"/>. When the runtime
/// cannot be started, or dies, the host carries on: actions are dropped with a
/// warning, filters return their input, and endpoints answer 503. A dead runtime is
/// restarted on next use, with exponential backoff capped at five minutes.</para>
///
/// <para>A runtime that stamps its <c>ctx.*</c> requests with the id of the host
/// call they serve (<c>host_call_id</c>, runtimes after 0.2.0, which say so in the
/// handshake) is called concurrently: each <c>ctx.*</c> request is answered through
/// the handler of the call it names, so it acts for that call's plugin and user
/// whatever else is in flight, and a ctx handler may itself call the runtime.</para>
///
/// <para>An older runtime is called one call at a time. While that call waits for
/// its answer, the runtime's <c>ctx.*</c> requests are answered through its
/// handler. A ctx handler cannot then call the runtime: such a nested call is
/// refused as unavailable (an action dropped, a filter returning its input).</para>
/// </summary>
public class PluginBridge : IDisposable
{
    public const string ProtocolVersion = "1.0";
    public static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan ManifestTimeout = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan ActionTimeout = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan FilterTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan EndpointTimeout = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan WebhookTimeout = TimeSpan.FromSeconds(60);

    private const int MaxMessageChars = 10 * 1024 * 1024;
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(5);
    private static readonly AsyncLocal<bool> InCall = new();

    private readonly PluginRuntimeOptions _options;
    private readonly string _workingDirectory;
    private readonly ILogger<PluginBridge> _logger;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _write = new(1, 1);

    private Process? _process;
    private Connection? _connection;
    private bool _httpContract;
    private IReadOnlyDictionary<string, PluginManifest>? _manifests;
    private int _failures;
    private DateTimeOffset _lastFailure;
    private long _nextId;
    private bool _disposed;

    public PluginBridge(IOptions<EscalatedOptions> options, ILogger<PluginBridge> logger, IHostEnvironment? environment = null,
        TimeProvider? time = null)
    {
        _options = options.Value.Plugins.Runtime;
        _workingDirectory = _options.WorkingDirectory ?? environment?.ContentRootPath ?? Directory.GetCurrentDirectory();
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    public bool IsEnabled => _options.Enabled;

    /// <summary>A call waiting for its answer: the handler for the ctx requests it causes.</summary>
    private sealed record Pending(string Method, PluginCtxHandler Ctx, TaskCompletionSource<JsonObject> Answer, CancellationToken Deadline);

    /// <summary>One running runtime: its calls in flight, by id, and what its handshake said.</summary>
    private sealed class Connection
    {
        public ConcurrentDictionary<long, Pending> Pending { get; } = new();
        public bool HostCallIds { get; set; }
        public bool Closed { get; set; }

        public void Close(string reason)
        {
            Closed = true;
            foreach (var call in Pending.Values)
            {
                call.Answer.TrySetException(new PluginBridgeException(reason));
            }
        }
    }

    /// <summary>Whether the running runtime is called concurrently (it stamps <c>host_call_id</c>).</summary>
    private bool Concurrent => _connection is { HostCallIds: true, Closed: false };

    /// <summary>The SDK plugins the runtime loaded, by name. Empty when off or unreachable.</summary>
    public virtual async Task<IReadOnlyDictionary<string, PluginManifest>> ManifestsAsync(CancellationToken ct = default)
    {
        if (!IsEnabled) return new Dictionary<string, PluginManifest>();
        if (_manifests is { } known) return known;
        if (InCall.Value && !Concurrent) return new Dictionary<string, PluginManifest>();

        await _gate.WaitAsync(ct);
        try
        {
            await EnsureRunningAsync(ct);
        }
        catch (PluginBridgeException)
        {
            // Logged; tried again on next use.
        }
        finally
        {
            _gate.Release();
        }

        return _manifests ?? new Dictionary<string, PluginManifest>();
    }

    public Task ActivateAsync(string plugin, PluginCtxHandler ctx, CancellationToken ct = default) =>
        RequestAsync("activate", new JsonObject { ["plugin"] = plugin }, ActionTimeout, ctx, ct);

    public Task DeactivateAsync(string plugin, PluginCtxHandler ctx, CancellationToken ct = default) =>
        RequestAsync("deactivate", new JsonObject { ["plugin"] = plugin }, ActionTimeout, ctx, ct);

    /// <summary>Fire-and-forget: a failure is logged, never thrown.</summary>
    public async Task ActionAsync(string hook, JsonNode? payload, IReadOnlyList<string> plugins, PluginCtxHandler ctx, CancellationToken ct = default)
    {
        try
        {
            await RequestAsync("action", new JsonObject
            {
                ["hook"] = hook,
                ["event"] = payload?.DeepClone(),
                ["plugins"] = Names(plugins),
            }, ActionTimeout, ctx, ct);
        }
        catch (PluginBridgeException e)
        {
            _logger.LogWarning("[Escalated plugins] action {Hook} was not delivered to SDK plugins: {Error}", hook, e.Message);
        }
    }

    /// <summary>The filtered value, or <paramref name="value"/> when the runtime fails.</summary>
    public async Task<JsonNode?> FilterAsync(string hook, JsonNode? value, IReadOnlyList<string> plugins, PluginCtxHandler ctx, CancellationToken ct = default)
    {
        try
        {
            return await RequestAsync("filter", new JsonObject
            {
                ["hook"] = hook,
                ["value"] = value?.DeepClone(),
                ["plugins"] = Names(plugins),
            }, FilterTimeout, ctx, ct);
        }
        catch (PluginBridgeException e)
        {
            _logger.LogWarning("[Escalated plugins] filter {Hook} skipped for SDK plugins: {Error}", hook, e.Message);
            return value;
        }
    }

    /// <summary>An endpoint (<paramref name="kind"/> <c>endpoint</c>) or webhook call.</summary>
    /// <exception cref="PluginBridgeException">The runtime is unavailable, or the handler threw.</exception>
    public async Task<JsonNode?> HttpAsync(string kind, string plugin, PluginHttpRequest request, PluginCtxHandler ctx, CancellationToken ct = default)
    {
        var parameters = request.ToJson();
        parameters["plugin"] = plugin;
        parameters["method"] = request.Method;
        parameters["path"] = request.Path;

        return await RequestAsync(kind, parameters, kind == "webhook" ? WebhookTimeout : EndpointTimeout, ctx, ct, needsHttpContract: true);
    }

    private static JsonArray Names(IEnumerable<string> plugins) => new(plugins.Select(p => (JsonNode?)p).ToArray());

    private async Task<JsonNode?> RequestAsync(string method, JsonObject parameters, TimeSpan timeout, PluginCtxHandler ctx,
        CancellationToken ct, bool needsHttpContract = false)
    {
        if (!IsEnabled) throw new PluginBridgeException("The plugin runtime is not enabled.", unavailable: true);
        if (InCall.Value && !Concurrent) throw new PluginBridgeException("The plugin runtime is busy with the call this one came from.", unavailable: true);

        await _gate.WaitAsync(ct);
        var holding = true;
        try
        {
            await EnsureRunningAsync(ct);
            if (needsHttpContract && !_httpContract)
            {
                throw new PluginBridgeException("Upgrade the plugin runtime to one that supports HTTP contract version 1.", unavailable: true);
            }

            var connection = _connection!;
            if (connection.HostCallIds)
            {
                // The runtime tells its ctx requests apart: other calls may go out
                // while this one waits.
                _gate.Release();
                holding = false;
            }

            try
            {
                return await CallAsync(connection, method, parameters, timeout, ctx, ct);
            }
            catch (PluginBridgeException e) when (!e.PluginError)
            {
                if (holding)
                {
                    // A timeout or a broken pipe: the runtime is in an unknown
                    // state, so the next call starts a fresh one.
                    Fail();
                }
                else if (!e.TimedOut)
                {
                    // A broken pipe. A timeout of one concurrent call says nothing
                    // about the others, so only this ends the runtime.
                    await _gate.WaitAsync(CancellationToken.None);
                    try
                    {
                        if (_connection == connection) Fail();
                    }
                    finally
                    {
                        _gate.Release();
                    }
                }

                throw;
            }
        }
        finally
        {
            if (holding) _gate.Release();
        }
    }

    /// <summary>Starts the runtime if it is not running: spawn, handshake, manifest. Called holding the gate.</summary>
    private async Task EnsureRunningAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_process is { HasExited: false }) return;
        Teardown();

        if (_failures > 0)
        {
            var backoff = TimeSpan.FromSeconds(Math.Min(5 * Math.Pow(2, _failures - 1), MaxBackoff.TotalSeconds));
            if (_time.GetUtcNow() - _lastFailure < backoff)
            {
                throw new PluginBridgeException("The plugin runtime failed recently; waiting before restarting it.", unavailable: true);
            }
        }

        try
        {
            Spawn();
            var connection = _connection!;
            PluginCtxHandler refuse = (m, _, _) => throw new InvalidOperationException($"{m} is not available during the handshake.");
            var answer = await CallAsync(connection, "handshake", new JsonObject
            {
                ["protocol_version"] = ProtocolVersion,
                ["host"] = "dotnet",
                ["host_version"] = typeof(PluginBridge).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
            }, HandshakeTimeout, refuse, ct);
            if (answer is not JsonObject hello || hello["compatible"] is not JsonValue compatible || compatible.GetValueKind() != JsonValueKind.True)
            {
                throw new PluginBridgeException($"The plugin runtime does not speak protocol {ProtocolVersion}.");
            }

            _httpContract = hello["http_contract_versions"] is JsonArray versions && versions.Any(v => PluginJson.Number(v) == 1);

            var manifests = await CallAsync(connection, "manifest", new JsonObject(), ManifestTimeout, refuse, ct);
            connection.HostCallIds = hello["host_call_ids"] is JsonValue stamps && stamps.GetValueKind() == JsonValueKind.True;
            _manifests = ReadManifests(manifests);
            _failures = 0;
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError("[Escalated plugins] could not start the plugin runtime: {Error}", e.Message);
            Fail();
            throw e as PluginBridgeException ?? new PluginBridgeException("The plugin runtime is not available.", unavailable: true);
        }
    }

    /// <summary>The runtime answers a map of name to manifest; older ones a list.</summary>
    private Dictionary<string, PluginManifest> ReadManifests(JsonNode? answer)
    {
        var entries = answer switch
        {
            JsonObject map => map.Select(pair => (Key: (string?)pair.Key, Value: pair.Value)),
            JsonArray list => list.Select(item => (Key: (string?)null, Value: item)),
            _ => Enumerable.Empty<(string?, JsonNode?)>(),
        };

        var manifests = new Dictionary<string, PluginManifest>(StringComparer.Ordinal);
        foreach (var (key, value) in entries)
        {
            if (value is not JsonObject manifest) continue;
            try
            {
                var parsed = PluginManifest.FromJson(manifest, key);
                manifests[parsed.Name] = parsed;
            }
            catch (InvalidOperationException e)
            {
                _logger.LogWarning("[Escalated plugins] skipped an SDK plugin: {Error}", e.Message);
            }
        }

        return manifests;
    }

    private void Spawn()
    {
        var info = new ProcessStartInfo(_options.Command)
        {
            WorkingDirectory = _workingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };
        foreach (var argument in _options.Arguments ?? new List<string> { PluginRuntimeOptions.DefaultEntryPoint }) info.ArgumentList.Add(argument);

        Process process;
        try
        {
            process = Process.Start(info) ?? throw new PluginBridgeException("Could not start the plugin runtime.", unavailable: true);
        }
        catch (Exception e) when (e is not PluginBridgeException)
        {
            throw new PluginBridgeException($"Could not start the plugin runtime ({e.Message}).", unavailable: true);
        }

        var connection = new Connection();
        _ = Task.Run(async () =>
        {
            try
            {
                while (await process.StandardOutput.ReadLineAsync() is { } line)
                {
                    if (line.Length > MaxMessageChars)
                    {
                        _logger.LogError("[Escalated plugins] the runtime sent a message over 10 MB; dropping the connection.");
                        break;
                    }

                    Receive(connection, line);
                }
            }
            catch (Exception)
            {
                // The pipe closed.
            }
            finally
            {
                connection.Close("The plugin runtime went away.");
            }
        });
        _ = Task.Run(async () =>
        {
            try
            {
                while (await process.StandardError.ReadLineAsync() is { } line)
                {
                    _logger.LogDebug("[Escalated plugin runtime] {Line}", line);
                }
            }
            catch (Exception)
            {
                // The pipe closed.
            }
        });

        _process = process;
        _connection = connection;
    }

    /// <summary>
    /// One line from the runtime: an answer goes to the call waiting for it; a
    /// <c>ctx.*</c> request is served, on its own task, through the handler of the
    /// call it serves.
    /// </summary>
    private void Receive(Connection connection, string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        JsonObject? message;
        try
        {
            message = JsonNode.Parse(line) as JsonObject;
        }
        catch (JsonException)
        {
            message = null;
        }

        if (message is null || PluginJson.String(message["jsonrpc"]) != "2.0")
        {
            _logger.LogWarning("[Escalated plugins] the runtime sent something that is not JSON-RPC: {Raw}", line.Length > 200 ? line[..200] : line);
            return;
        }

        if (message.ContainsKey("method"))
        {
            var call = Serving(connection, message);
            _ = Task.Run(() => ServeAsync(message, call));
            return;
        }

        if (PluginJson.Number(message["id"]) is not { } id || !connection.Pending.TryGetValue((long)id, out var waiting))
        {
            _logger.LogWarning("[Escalated plugins] the runtime answered a request nobody is waiting for ({Id}).", message["id"]?.ToJsonString());
            return;
        }

        waiting.Answer.TrySetResult(message);
    }

    /// <summary>
    /// The call a <c>ctx.*</c> request serves: the one its <c>host_call_id</c> names,
    /// else (an older runtime) the only call in flight. Null when it cannot tell.
    /// </summary>
    private static Pending? Serving(Connection connection, JsonObject message)
    {
        var parameters = message["params"] as JsonObject;
        if (parameters?["host_call_id"] is { } stamped)
        {
            parameters.Remove("host_call_id");
            if (PluginJson.Number(stamped) is { } hostCall && connection.Pending.TryGetValue((long)hostCall, out var named))
            {
                return named;
            }
        }

        var inFlight = connection.Pending.Values.Take(2).ToList();
        return inFlight.Count == 1 ? inFlight[0] : null;
    }

    /// <summary>
    /// Sends a request and waits for its answer; the runtime's <c>ctx.*</c>
    /// requests for it are served through <paramref name="ctx"/> meanwhile.
    /// </summary>
    private async Task<JsonNode?> CallAsync(Connection connection, string method, JsonObject parameters, TimeSpan timeout,
        PluginCtxHandler ctx, CancellationToken ct)
    {
        if (connection.Closed) throw new PluginBridgeException($"The plugin runtime went away before {method}.");

        var id = Interlocked.Increment(ref _nextId);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);
        var call = new Pending(method, ctx, new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously), deadline.Token);
        connection.Pending[id] = call;
        try
        {
            await WriteAsync(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = parameters, ["id"] = id });

            JsonObject message;
            try
            {
                message = await call.Answer.Task.WaitAsync(deadline.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new PluginBridgeException($"Timed out after {timeout.TotalSeconds:0} s waiting for the plugin runtime to answer {method}.") { TimedOut = true };
            }
            catch (PluginBridgeException)
            {
                throw new PluginBridgeException($"The plugin runtime went away while answering {method}.");
            }

            if (message["error"] is { } error)
            {
                var text = error is JsonObject e ? PluginJson.String(e["message"]) : null;
                throw new PluginBridgeException($"The plugin runtime answered with an error: {text ?? "unknown error"}", pluginError: true);
            }

            return message["result"]?.DeepClone();
        }
        finally
        {
            connection.Pending.TryRemove(id, out _);
        }
    }

    private async Task ServeAsync(JsonObject message, Pending? call)
    {
        var id = message["id"]?.DeepClone();
        var method = PluginJson.String(message["method"]) ?? string.Empty;
        var parameters = message["params"] as JsonObject ?? new JsonObject();

        JsonObject reply;
        InCall.Value = true;
        try
        {
            if (call is null)
            {
                throw new InvalidOperationException($"{method} names no host call in flight.");
            }

            var result = await call.Ctx(method, (JsonObject)parameters.DeepClone(), call.Deadline);
            reply = new JsonObject { ["jsonrpc"] = "2.0", ["result"] = result, ["id"] = id };
        }
        catch (Exception e) when (e is not OperationCanceledException || call is null || !call.Deadline.IsCancellationRequested)
        {
            _logger.LogWarning("[Escalated plugins] {Method} failed: {Error}", method, e.Message);
            reply = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["error"] = new JsonObject { ["code"] = -32000, ["message"] = e.Message },
                ["id"] = id,
            };
        }
        catch (OperationCanceledException)
        {
            // The call this served gave up; nobody waits for the answer.
            return;
        }
        finally
        {
            InCall.Value = false;
        }

        if (id is null) return;
        try
        {
            await WriteAsync(reply);
        }
        catch (PluginBridgeException e)
        {
            _logger.LogDebug("[Escalated plugins] answering {Method} failed: {Error}", method, e.Message);
        }
    }

    private async Task WriteAsync(JsonObject message)
    {
        var line = message.ToJsonString();
        if (line.Length > MaxMessageChars) throw new PluginBridgeException("A message to the plugin runtime would be over 10 MB.", pluginError: true);

        var process = _process ?? throw new PluginBridgeException("The plugin runtime is not running.", unavailable: true);
        await _write.WaitAsync();
        try
        {
            await process.StandardInput.WriteAsync(line + "\n");
            await process.StandardInput.FlushAsync();
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException)
        {
            throw new PluginBridgeException("Could not write to the plugin runtime.");
        }
        finally
        {
            _write.Release();
        }
    }

    private void Fail()
    {
        _failures++;
        _lastFailure = _time.GetUtcNow();
        Teardown();
    }

    private void Teardown()
    {
        var process = _process;
        _process = null;
        _connection?.Close("The plugin runtime was stopped.");
        _connection = null;
        _httpContract = false;
        if (process is null) return;

        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
            // Already gone.
        }

        process.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Teardown();
        _gate.Dispose();
        _write.Dispose();
        GC.SuppressFinalize(this);
    }
}
