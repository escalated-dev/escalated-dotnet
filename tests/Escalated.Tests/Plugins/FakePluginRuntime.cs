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
/// <para><c>--die</c> exits at once, like a runtime that cannot start;
/// <c>--die-on-endpoint</c> exits when an endpoint or webhook is called.</para>
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
        var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        var nextId = 1;

        void Send(JsonObject message) => stdout.WriteLine(message.ToJsonString());

        JsonNode? Ask(string method, JsonObject parameters)
        {
            var id = "rt-" + nextId++;
            Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters });
            while (stdin.ReadLine() is { } line)
            {
                if (JsonNode.Parse(line) is JsonObject answer && answer["id"]?.ToString() == id && !answer.ContainsKey("method"))
                {
                    return answer["error"] ?? answer["result"];
                }
            }

            return null;
        }

        while (stdin.ReadLine() is { } line)
        {
            if (JsonNode.Parse(line) is not JsonObject message || message["method"] is null) continue;
            var method = message["method"]!.GetValue<string>();
            var p = message["params"] as JsonObject ?? new JsonObject();
            if (log is not null)
            {
                File.AppendAllText(log, new JsonObject { ["method"] = method, ["params"] = p.DeepClone() }.ToJsonString() + "\n");
            }

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
                                new JsonObject { ["method"] = "GET", ["path"] = "/boom" }),
                            ["webhooks"] = new JsonArray(new JsonObject { ["method"] = "POST", ["path"] = "/hook" }),
                            ["cronSchedules"] = new JsonArray(),
                        },
                    };
                    break;

                case "endpoint":
                case "webhook":
                    if (args.Contains("--die-on-endpoint")) return 1;
                    var path = p["path"]?.GetValue<string>();
                    if (path == "/SdkDash")
                    {
                        result = new JsonObject { ["widgets"] = 3 };
                    }
                    else if (path == "/boom")
                    {
                        error = new JsonObject { ["code"] = -32000, ["message"] = "kaboom secret" };
                    }
                    else if (path == "/peek")
                    {
                        // Names another plugin; the host must keep it to this one.
                        Ask("ctx.store.set", new JsonObject { ["plugin"] = CrmPlugin.Name, ["collection"] = "notes", ["key"] = "k", ["value"] = "from-sdk" });
                        result = new JsonObject
                        {
                            ["stored"] = Ask("ctx.store.get", new JsonObject { ["plugin"] = Name, ["collection"] = "notes", ["key"] = "k" })?.DeepClone(),
                            ["user"] = Ask("ctx.currentUser", new JsonObject { ["plugin"] = Name })?.DeepClone(),
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
                        Ask("ctx.store.insert", new JsonObject
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
