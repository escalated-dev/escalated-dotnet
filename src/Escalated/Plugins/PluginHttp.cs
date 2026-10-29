using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Escalated.Ui;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Escalated.Plugins;

/// <summary>
/// What a plugin endpoint or webhook is given: HTTP contract version 1 (the SDK's
/// <c>docs/http-contract.md</c>). Credentials are never in <see cref="Headers"/>.
/// </summary>
public sealed class PluginHttpRequest
{
    /// <summary>The declared method (a HEAD request is served by a GET route).</summary>
    public required string Method { get; init; }

    /// <summary>The manifest path, with its <c>:param</c> segments.</summary>
    public required string Path { get; init; }

    /// <summary>The values of the path's <c>:param</c> segments.</summary>
    public IReadOnlyDictionary<string, string> Params { get; init; } = new Dictionary<string, string>();

    public IReadOnlyDictionary<string, string> Query { get; init; } = new Dictionary<string, string>();

    /// <summary>Lower-case names; <c>Cookie</c>, <c>Authorization</c>, the CSRF tokens and <c>Proxy-*</c> removed.</summary>
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();

    /// <summary>A JSON body parsed, a form body as an object, otherwise null.</summary>
    public JsonNode? Body { get; init; }

    /// <summary>The body exactly as sent, base64-encoded, for signature checks.</summary>
    public string RawBodyBase64 { get; init; } = string.Empty;

    public string? ClientIp { get; init; }

    /// <summary>The wire form the plugin runtime receives (without method and path, which the caller adds).</summary>
    public JsonObject ToJson()
    {
        var json = new JsonObject
        {
            ["httpContract"] = 1,
            ["rawBodyBase64"] = RawBodyBase64,
            ["body"] = Body?.DeepClone(),
            ["params"] = ToObject(Params),
            ["query"] = ToObject(Query),
            ["headers"] = ToObject(Headers),
        };

        // The runtime refuses a clientIp that is present but not a string.
        if (ClientIp is not null) json["clientIp"] = ClientIp;
        return json;
    }

    private static JsonObject ToObject(IReadOnlyDictionary<string, string> values)
    {
        var obj = new JsonObject();
        foreach (var (key, value) in values) obj[key] = value;
        return obj;
    }
}

/// <summary>
/// The SDK's <c>httpResponse()</c> envelope: a handler's answer with its own status,
/// format and headers. Any other return value is JSON with 200.
/// </summary>
public sealed class PluginHttpResponse
{
    public int Status { get; init; } = 200;

    /// <summary><c>json</c> or <c>text</c>.</summary>
    public string Format { get; init; } = "json";

    public object? Body { get; init; }
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();

    public static PluginHttpResponse Json(object? body, int status = 200, IReadOnlyDictionary<string, string>? headers = null) =>
        new() { Status = status, Body = body, Headers = headers ?? new Dictionary<string, string>() };

    public static PluginHttpResponse Text(string body, int status = 200, IReadOnlyDictionary<string, string>? headers = null) =>
        new() { Status = status, Format = "text", Body = body, Headers = headers ?? new Dictionary<string, string>() };

    internal JsonObject ToEnvelope()
    {
        var headers = new JsonObject();
        foreach (var (name, value) in Headers) headers[name] = value;
        return new JsonObject
        {
            ["__escalated_http"] = 1,
            ["status"] = Status,
            ["format"] = Format,
            ["headers"] = headers,
            ["body"] = PluginJson.ToNode(Body),
        };
    }
}

/// <summary>Turns requests into <see cref="PluginHttpRequest"/> and handler results into responses.</summary>
public static class PluginHttp
{
    private static readonly HashSet<string> Withheld = new(StringComparer.OrdinalIgnoreCase)
    {
        "cookie", "authorization", "x-xsrf-token", "x-csrf-token",
    };

    private static readonly HashSet<string> BlockedResponseHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "connection", "content-length", "transfer-encoding", "keep-alive", "upgrade", "proxy-authenticate",
        "proxy-authorization", "te", "trailer", "set-cookie",
    };

    private static readonly Regex HeaderName = new("^[!#$%&'*+.^_`|~0-9a-z-]+$", RegexOptions.Compiled);

    /// <summary>Reads the request into contract-1 form. The body is read in full and left readable.</summary>
    public static async Task<PluginHttpRequest> ReadAsync(HttpRequest request, PluginRoute route,
        IReadOnlyDictionary<string, string> parameters, CancellationToken ct)
    {
        request.EnableBuffering();
        byte[] raw;
        using (var buffer = new MemoryStream())
        {
            await request.Body.CopyToAsync(buffer, ct);
            raw = buffer.ToArray();
        }

        request.Body.Position = 0;

        JsonNode? body = null;
        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync(ct);
            var obj = new JsonObject();
            foreach (var (key, value) in form) obj[key] = value.Count == 1 ? value[0] : new JsonArray(value.Select(v => (JsonNode?)v).ToArray());
            body = form.Count == 0 ? null : obj;
        }
        else if (raw.Length > 0 && request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true)
        {
            try
            {
                body = JsonNode.Parse(raw);
            }
            catch (JsonException)
            {
                body = null;
            }
        }

        var headers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, values) in request.Headers)
        {
            var lower = name.ToLowerInvariant();
            if (Withheld.Contains(lower) || lower.StartsWith("proxy-", StringComparison.Ordinal)) continue;
            headers[lower] = string.Join(", ", values.ToArray());
        }

        return new PluginHttpRequest
        {
            Method = route.Method,
            Path = route.Path,
            Params = parameters,
            Query = request.Query.ToDictionary(q => q.Key, q => q.Value.ToString(), StringComparer.Ordinal),
            Headers = headers,
            Body = body,
            RawBodyBase64 = Convert.ToBase64String(raw),
            ClientIp = request.HttpContext.Connection.RemoteIpAddress?.ToString(),
        };
    }

    /// <summary>
    /// The declared route matching the request, and the values of its <c>:param</c>
    /// segments. A HEAD request falls back to a GET route.
    /// </summary>
    public static (PluginRoute Route, Dictionary<string, string> Params)? Match(IEnumerable<PluginRoute> routes, string method, string path)
    {
        var actual = Segments(path);
        var list = routes.ToList();
        foreach (var candidate in method == "HEAD" ? new[] { "HEAD", "GET" } : new[] { method })
        {
            foreach (var route in list.Where(r => r.Method == candidate))
            {
                var pattern = Segments(route.Path);
                if (pattern.Length != actual.Length) continue;

                var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
                var matched = true;
                for (var i = 0; i < pattern.Length && matched; i++)
                {
                    if (pattern[i].Length > 1 && pattern[i][0] == ':') parameters[pattern[i][1..]] = Uri.UnescapeDataString(actual[i]);
                    else matched = pattern[i] == actual[i];
                }

                if (matched) return (route, parameters);
            }
        }

        return null;
    }

    /// <summary>
    /// A handler's ordinary value is JSON with 200; the <c>httpResponse()</c> envelope
    /// sets status, format and headers. A malformed envelope is a 502.
    /// </summary>
    public static IActionResult Response(JsonNode? result)
    {
        if (result is not JsonObject envelope || !envelope.ContainsKey("__escalated_http"))
        {
            return Json(result ?? new JsonObject(), 200, null);
        }

        var status = Int(envelope["status"]);
        var format = Str(envelope["format"]);
        if (Int(envelope["__escalated_http"]) != 1 || status is null or < 200 or > 599 || format is not ("json" or "text")
            || envelope["headers"] is not JsonObject headers || !envelope.ContainsKey("body")
            || (format == "text" && Str(envelope["body"]) is null))
        {
            return Invalid();
        }

        var outHeaders = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (rawName, value) in headers)
        {
            var name = rawName.ToLowerInvariant();
            var text = Str(value);
            if (!HeaderName.IsMatch(name) || BlockedResponseHeaders.Contains(name) || outHeaders.ContainsKey(name)
                || text is null || text.Any(c => c < 0x20 || c == 0x7f))
            {
                return Invalid();
            }

            outHeaders[name] = text;
        }

        if (format == "json") return Json(envelope["body"], status.Value, outHeaders);

        outHeaders.TryAdd("content-type", "text/plain; charset=utf-8");
        return new RawResult(Encoding.UTF8.GetBytes(Str(envelope["body"])!), status.Value, outHeaders);
    }

    /// <summary>The <c>props</c> a page gets from its plugin's <c>GET /{component}</c> answer: a JSON object, or <c>{}</c>.</summary>
    public static JsonObject PageProps(JsonNode? result)
    {
        if (result is JsonObject envelope && envelope.ContainsKey("__escalated_http"))
        {
            var ok = Int(envelope["status"]) is < 300 && Str(envelope["format"]) == "json";
            result = ok ? envelope["body"] : null;
        }

        return result is JsonObject props ? (JsonObject)props.DeepClone() : new JsonObject();
    }

    public static IActionResult Message(int status, string message) =>
        Json(new JsonObject { ["message"] = message }, status, null);

    private static IActionResult Json(JsonNode? body, int status, IReadOnlyDictionary<string, string>? headers)
    {
        var all = new Dictionary<string, string>(headers ?? new Dictionary<string, string>(), StringComparer.Ordinal);
        all.TryAdd("content-type", "application/json; charset=utf-8");
        return new RawResult(Encoding.UTF8.GetBytes(body?.ToJsonString() ?? "null"), status, all);
    }

    private static IActionResult Invalid() => Message(StatusCodes.Status502BadGateway, "Invalid plugin HTTP response.");

    private static string[] Segments(string path) =>
        path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

    private static int? Int(JsonNode? node) => PluginJson.Number(node) is { } d && d == Math.Floor(d) && Math.Abs(d) < int.MaxValue ? (int)d : null;

    private static string? Str(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private sealed class RawResult : IActionResult
    {
        private readonly byte[] _body;
        private readonly int _status;
        private readonly IReadOnlyDictionary<string, string> _headers;

        public RawResult(byte[] body, int status, IReadOnlyDictionary<string, string> headers)
        {
            _body = body;
            _status = status;
            _headers = headers;
        }

        public async Task ExecuteResultAsync(ActionContext context)
        {
            var response = context.HttpContext.Response;
            response.StatusCode = _status;
            foreach (var (name, value) in _headers)
            {
                if (name == "content-type") response.ContentType = value;
                else response.Headers[name] = value;
            }

            if (HttpMethods.IsHead(context.HttpContext.Request.Method)) return;
            await response.Body.WriteAsync(_body, context.HttpContext.RequestAborted);
        }
    }
}

/// <summary>JSON conversions for plugin values: snake_case for .NET objects, nodes as they are.</summary>
public static class PluginJson
{
    public static JsonNode? ToNode(object? value) => value switch
    {
        null => null,
        JsonNode node => node.DeepClone(),
        JsonElement element => JsonNode.Parse(element.GetRawText()),
        PluginHttpResponse response => response.ToEnvelope(),
        _ => JsonSerializer.SerializeToNode(value, UiJson.Options),
    };

    /// <summary>A JSON number's value, however the node was built (parsed or from a .NET number).</summary>
    public static double? Number(JsonNode? node) =>
        node is JsonValue v && v.GetValueKind() == JsonValueKind.Number
            ? double.Parse(v.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture)
            : null;

    /// <summary>A JSON string's value.</summary>
    public static string? String(JsonNode? node) =>
        node is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null;
}
