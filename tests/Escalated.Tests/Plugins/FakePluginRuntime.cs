using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Nodes;

namespace Escalated.Tests.Plugins;

/// <summary>
/// A stand-in for <c>@escalated-dev/plugin-runtime</c>: this test assembly run as a
/// program (<c>dotnet Escalated.Tests.dll --fake-plugin-runtime &lt;log&gt;</c>). It
/// speaks the bridge protocol over stdio with one SDK plugin,
/// <c>@acme/sdk-fixture</c>, and appends every message it receives to the log file,
/// so a test can see exactly what the host sent. The real runtime is tested in its
/// own repository; this exercises the host's side of the bridge.
///
/// <para>Like the real runtime it serves host calls concurrently, and (as runtimes
/// after 0.2.0 do) stamps every <c>ctx.*</c> request it makes while handling a host
/// call with that call's id as <c>host_call_id</c>, answering the handshake with
/// <c>host_call_ids: true</c>. <c>--no-host-call-ids</c> turns both off, as an older
/// runtime.</para>
///
/// <para><c>--die</c> exits at once, like a runtime that cannot start;
/// <c>--die-on-endpoint</c> exits when an endpoint or webhook is called. The
/// <c>/together</c> endpoint waits for a second <c>/together</c> call (three seconds
/// at most, then fails with "alone") and answers <c>ctx.currentUser</c>, so two
/// calls are in flight at once.</para>
/// </summary>
public static class FakePluginRuntime
{
    public const string Flag = "--fake-plugin-runtime";
    public const string Name = "@acme/sdk-fixture";
    public const string Slug = "acme--sdk-fixture";

    public static int Run(string[] args)
    {
        if (args.Contains("--die")) return 1;

        var log = args.Length > 1 && !args[1].StartsWith("--", StringComparison.Ordinal) ? args[1] : null;
        var stamp = !args.Contains("--no-host-call-ids");
        var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        var writeLock = new object();
        var pending = new ConcurrentDictionary<string, TaskCompletionSource<JsonNode?>>();
        var nextId = 0;
        var togetherLock = new object();
        TaskCompletionSource? together = null;
        var running = new List<Task>();

        void Send(JsonObject message)
        {
            lock (writeLock) stdout.WriteLine(message.ToJsonString());
        }

        JsonNode? Ask(JsonNode? serving, string method, JsonObject parameters)
        {
            var id = "rt-" + Interlocked.Increment(ref nextId);
            if (stamp && serving is not null) parameters["host_call_id"] = serving.DeepClone();
            var answer = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
            pending[id] = answer;
            Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters });
            return answer.Task.Wait(TimeSpan.FromSeconds(20)) ? answer.Task.Result : null;
        }

        bool Meet()
        {
            TaskCompletionSource mine;
            lock (togetherLock)
            {
                if (together is { } waiting)
                {
                    together = null;
                    waiting.TrySetResult();
                    return true;
                }

                mine = together = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            if (mine.Task.Wait(TimeSpan.FromSeconds(3))) return true;
            lock (togetherLock)
            {
                if (together == mine) together = null;
            }

            return mine.Task.IsCompleted;
        }

        void Handle(JsonObject message)
        {
            var method = message["method"]!.GetValue<string>();
            var p = message["params"] as JsonObject ?? new JsonObject();
            var serving = message["id"];

            JsonNode? result = new JsonObject { ["ok"] = true };
            JsonObject? error = null;

            switch (method)
            {
                case "handshake":
                    result = new JsonObject
                    {
                        ["compatible"] = p["protocol_version"]?.GetValue<string>() == "1.0",
                        ["protocol_version"] = "1.0",
                        ["runtime_version"] = "fixture",
                        ["http_contract_versions"] = new JsonArray(1),
                        ["host_call_ids"] = stamp,
                    };
                    break;

                case "manifest":
                    result = new JsonObject
                    {
                        [Name] = new JsonObject
                        {
                            ["name"] = Name,
                            ["version"] = "0.4.0",
                            ["description"] = "An SDK plugin behind the bridge.",
                            ["config"] = new JsonArray(),
                            ["pages"] = new JsonArray(new JsonObject
                            {
                                ["route"] = "dash",
                                ["component"] = "SdkDash",
                                ["menu"] = new JsonObject { ["label"] = "SDK", ["section"] = "admin", ["position"] = 1 },
                            }),
                            ["components"] = new JsonArray(),
                            ["widgets"] = new JsonArray(new JsonObject { ["component"] = "SdkWidget", ["label"] = "SDK", ["size"] = "full" }),
                            ["actionHooks"] = new JsonArray("ticket.created", "plugin.activated", "plugin.deactivated"),
                            ["filterHooks"] = new JsonArray(new JsonObject { ["hook"] = "demo.value", ["priority"] = 15 }),
                            ["endpoints"] = new JsonArray(
                                new JsonObject { ["method"] = "GET", ["path"] = "/SdkDash" },
                                new JsonObject { ["method"] = "POST", ["path"] = "/items/:id" },
                                new JsonObject { ["method"] = "GET", ["path"] = "/peek" },
                                new JsonObject { ["method"] = "GET", ["path"] = "/together" },
                                new JsonObject { ["method"] = "GET", ["path"] = "/boom" }),
                            ["webhooks"] = new JsonArray(new JsonObject { ["method"] = "POST", ["path"] = "/hook" }),
                            ["cronSchedules"] = new JsonArray(),
                        },
                    };
                    break;

                case "endpoint":
                case "webhook":
                    if (args.Contains("--die-on-endpoint")) Environment.Exit(1);
                    var path = p["path"]?.GetValue<string>();
                    if (path == "/SdkDash")
                    {
                        result = new JsonObject { ["widgets"] = 3 };
                    }
                    else if (path == "/boom")
                    {
                        error = new JsonObject { ["code"] = -32000, ["message"] = "kaboom secret" };
                    }
                    else if (path == "/together")
                    {
                        if (!Meet())
                        {
                            error = new JsonObject { ["code"] = -32000, ["message"] = "alone" };
                            break;
                        }

                        result = new JsonObject { ["user"] = Ask(serving, "ctx.currentUser", new JsonObject { ["plugin"] = Name })?.DeepClone() };
                    }
                    else if (path == "/peek")
                    {
                        // Names another plugin; the host must keep it to this one.
                        Ask(serving, "ctx.store.set", new JsonObject { ["plugin"] = CrmPlugin.Name, ["collection"] = "notes", ["key"] = "k", ["value"] = "from-sdk" });
                        result = new JsonObject
                        {
                            ["stored"] = Ask(serving, "ctx.store.get", new JsonObject { ["plugin"] = Name, ["collection"] = "notes", ["key"] = "k" })?.DeepClone(),
                            ["user"] = Ask(serving, "ctx.currentUser", new JsonObject { ["plugin"] = Name })?.DeepClone(),
                        };
                    }
                    else
                    {
                        result = new JsonObject
                        {
                            ["__escalated_http"] = 1,
                            ["status"] = 201,
                            ["format"] = "json",
                            ["headers"] = new JsonObject { ["x-runtime"] = "fixture" },
                            ["body"] = new JsonObject
                            {
                                ["kind"] = method,
                                ["path"] = path,
                                ["params"] = p["params"]?.DeepClone(),
                                ["query"] = p["query"]?.DeepClone(),
                                ["body"] = p["body"]?.DeepClone(),
                                ["header_names"] = new JsonArray((p["headers"] as JsonObject ?? new JsonObject()).Select(h => (JsonNode?)h.Key).ToArray()),
                            },
                        };
                    }

                    break;

                case "filter":
                    var plugins = (p["plugins"] as JsonArray ?? new JsonArray()).Select(n => n?.GetValue<string>()).ToList();
                    var value = p["value"]?.GetValue<string>() ?? string.Empty;
                    result = plugins.Contains(Name) ? value + "|sdk" : value;
                    break;

                case "action":
                    if (p["hook"]?.GetValue<string>() == "ticket.created")
                    {
                        Ask(serving, "ctx.store.insert", new JsonObject
                        {
                            ["plugin"] = Name,
                            ["collection"] = "seen",
                            ["data"] = new JsonObject { ["ticket"] = p["event"]?["id"]?.DeepClone() },
                        });
                    }

                    break;
            }

            if (message["id"] is { } messageId)
            {
                Send(error is null
                    ? new JsonObject { ["jsonrpc"] = "2.0", ["id"] = messageId.DeepClone(), ["result"] = result }
                    : new JsonObject { ["jsonrpc"] = "2.0", ["id"] = messageId.DeepClone(), ["error"] = error });
            }
        }

        while (stdin.ReadLine() is { } line)
        {
            if (JsonNode.Parse(line) is not JsonObject message) continue;
            if (message["method"] is null)
            {
                // An answer to one of our ctx requests.
                if (message["id"]?.ToString() is { } answered && pending.TryRemove(answered, out var waiting))
                {
                    waiting.TrySetResult(message["error"]?.DeepClone() ?? message["result"]?.DeepClone());
                }

                continue;
            }

            if (log is not null)
            {
                lock (writeLock)
                {
                    File.AppendAllText(log, new JsonObject { ["method"] = message["method"]!.DeepClone(), ["params"] = message["params"]?.DeepClone() ?? new JsonObject() }.ToJsonString() + "\n");
                }
            }

            running.Add(Task.Run(() => Handle(message)));
        }

        Task.WaitAll(running.ToArray(), TimeSpan.FromSeconds(5));
        return 0;
    }
}

/// <summary>
/// The test assembly's entry point. The test runner never calls it; it exists so
/// the assembly can run as <see cref="FakePluginRuntime"/>.
/// </summary>
public static class TestProgram
{
    public static int Main(string[] args) =>
        args.Length > 0 && args[0] == FakePluginRuntime.Flag ? FakePluginRuntime.Run(args) : 0;
}
