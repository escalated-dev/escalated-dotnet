using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Escalated.Data;
using Escalated.Models;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Plugins;

/// <summary>
/// <c>ctx.config</c> and <c>ctx.store</c>, always for one plugin name.
///
/// <para>Queries filter in .NET over the plugin's rows in one collection, so a
/// field name never reaches SQL and the operators mean the same on every
/// database.</para>
/// </summary>
public sealed class PluginStore
{
    public const string Config = "__config__";

    private static readonly Regex Field = new(@"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z0-9_]+)*$", RegexOptions.Compiled);
    private static readonly HashSet<string> Operators = new(StringComparer.Ordinal) { "$gt", "$gte", "$lt", "$lte", "$ne", "$in", "$nin" };

    private readonly EscalatedDbContext _db;

    public PluginStore(EscalatedDbContext db)
    {
        _db = db;
    }

    public async Task<JsonObject> ConfigAllAsync(string plugin, CancellationToken ct = default) =>
        (await RecordAsync(plugin, Config, Config, ct))?.Data is { } data && JsonNode.Parse(data) is JsonObject obj ? obj : new JsonObject();

    public async Task<JsonNode?> ConfigGetAsync(string plugin, string key, CancellationToken ct = default) =>
        (await ConfigAllAsync(plugin, ct))[key]?.DeepClone();

    /// <summary>Merges <paramref name="data"/> into the plugin's config.</summary>
    public async Task ConfigSetAsync(string plugin, JsonObject data, CancellationToken ct = default)
    {
        var config = await ConfigAllAsync(plugin, ct);
        foreach (var (key, value) in data) config[key] = value?.DeepClone();
        await PutAsync(plugin, Config, Config, config, ct);
    }

    public async Task<JsonNode?> GetAsync(string plugin, string collection, string key, CancellationToken ct = default) =>
        (await RecordAsync(plugin, collection, key, ct))?.Data is { } data ? JsonNode.Parse(data) : null;

    public Task SetAsync(string plugin, string collection, string key, JsonNode? value, CancellationToken ct = default) =>
        PutAsync(plugin, collection, key, value, ct);

    /// <summary>
    /// The plugin's documents in <paramref name="collection"/> matching
    /// <paramref name="filter"/>: equality (an array field matches when it holds the
    /// value) and <c>$gt $gte $lt $lte $ne $in $nin</c> on dotted field names.
    /// <paramref name="options"/> takes <c>limit</c>, <c>orderBy</c> and <c>order</c>.
    /// Each document is its data with <c>_id</c>.
    /// </summary>
    public async Task<JsonArray> QueryAsync(string plugin, string collection, JsonObject? filter = null, JsonObject? options = null,
        CancellationToken ct = default)
    {
        var conditions = new List<(string Field, string Operator, JsonNode? Operand)>();
        foreach (var (field, condition) in filter ?? new JsonObject())
        {
            AssertField(field);
            if (condition is JsonObject ops && ops.Count > 0 && ops.All(op => op.Key.StartsWith('$')))
            {
                foreach (var (op, operand) in ops)
                {
                    if (!Operators.Contains(op)) throw new ArgumentException($"Unsupported store query operator: {op}");
                    conditions.Add((field, op, operand));
                }
            }
            else
            {
                conditions.Add((field, "$eq", condition));
            }
        }

        string? orderBy = null;
        if (options?["orderBy"] is JsonValue order && order.TryGetValue<string>(out var by))
        {
            AssertField(by);
            orderBy = by;
        }

        var documents = new List<JsonObject>();
        foreach (var record in await RecordsAsync(plugin, collection, ct))
        {
            var document = Document(record);
            if (conditions.All(c => Matches(Path(document, c.Field), c.Operator, c.Operand))) documents.Add(document);
        }

        if (orderBy is not null)
        {
            var descending = options?["order"] is JsonValue o && o.TryGetValue<string>(out var dir) && dir.Equals("desc", StringComparison.OrdinalIgnoreCase);
            documents = documents
                .Select((d, i) => (d, i, v: Path(d, orderBy)))
                .OrderBy(x => x, Comparer<(JsonObject d, int i, Lookup v)>.Create((a, b) =>
                {
                    // Documents without the field come last either way.
                    if (a.v.Missing || b.v.Missing) return a.v.Missing.CompareTo(b.v.Missing) is var m && m != 0 ? m : a.i.CompareTo(b.i);
                    var cmp = CompareForSort(a.v.Value, b.v.Value);
                    if (descending) cmp = -cmp;
                    return cmp != 0 ? cmp : a.i.CompareTo(b.i);
                }))
                .Select(x => x.d)
                .ToList();
        }

        if (PluginJson.Number(options?["limit"]) is { } limit)
        {
            documents = documents.Take(Math.Max(0, (int)limit)).ToList();
        }

        return new JsonArray(documents.Select(d => (JsonNode?)d).ToArray());
    }

    /// <summary>A new document; its key is <c>data.key</c>, when it has one.</summary>
    public async Task<JsonObject> InsertAsync(string plugin, string collection, JsonObject data, CancellationToken ct = default)
    {
        var key = data["key"] is JsonValue k ? (k.TryGetValue<string>(out var s) ? s : k.ToJsonString()) : null;
        var record = new PluginStoreRecord
        {
            Plugin = plugin,
            Collection = collection,
            Key = key,
            Data = data.ToJsonString(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        _db.PluginStore.Add(record);
        await _db.SaveChangesAsync(ct);
        return Document(record);
    }

    /// <summary>Merges <paramref name="data"/> into the document with this key.</summary>
    public async Task<JsonObject> UpdateAsync(string plugin, string collection, string key, JsonObject data, CancellationToken ct = default)
    {
        var record = await RecordAsync(plugin, collection, key, ct)
            ?? throw new ArgumentException($"No {collection} record with key {key}.");
        var existing = record.Data is { } text && JsonNode.Parse(text) is JsonObject obj ? obj : new JsonObject();
        foreach (var (field, value) in data) existing[field] = value?.DeepClone();
        record.Data = existing.ToJsonString();
        record.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Document(record);
    }

    public async Task DeleteAsync(string plugin, string collection, string key, CancellationToken ct = default)
    {
        var records = await _db.PluginStore.Where(r => r.Plugin == plugin && r.Collection == collection && r.Key == key).ToListAsync(ct);
        _db.PluginStore.RemoveRange(records);
        await _db.SaveChangesAsync(ct);
    }

    public static void AssertField(string field)
    {
        if (!Field.IsMatch(field)) throw new ArgumentException($"Invalid store field name: {field}");
    }

    private async Task PutAsync(string plugin, string collection, string key, JsonNode? value, CancellationToken ct)
    {
        var json = value?.ToJsonString() ?? "null";
        var record = await RecordAsync(plugin, collection, key, ct);
        if (record is null)
        {
            _db.PluginStore.Add(new PluginStoreRecord
            {
                Plugin = plugin,
                Collection = collection,
                Key = key,
                Data = json,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
        }
        else
        {
            record.Data = json;
            record.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
    }

    private Task<PluginStoreRecord?> RecordAsync(string plugin, string collection, string key, CancellationToken ct) =>
        _db.PluginStore.Where(r => r.Plugin == plugin && r.Collection == collection && r.Key == key)
            .OrderBy(r => r.Id).FirstOrDefaultAsync(ct);

    private Task<List<PluginStoreRecord>> RecordsAsync(string plugin, string collection, CancellationToken ct) =>
        _db.PluginStore.AsNoTracking().Where(r => r.Plugin == plugin && r.Collection == collection)
            .OrderBy(r => r.Id).ToListAsync(ct);

    private static JsonObject Document(PluginStoreRecord record)
    {
        var document = new JsonObject { ["_id"] = record.Id };
        if (record.Data is { } text && JsonNode.Parse(text) is JsonObject data)
        {
            foreach (var (key, value) in data)
            {
                if (key != "_id") document[key] = value?.DeepClone();
            }
        }

        return document;
    }

    private readonly record struct Lookup(bool Missing, JsonNode? Value);

    private static Lookup Path(JsonObject document, string field)
    {
        JsonNode? value = document;
        foreach (var segment in field.Split('.'))
        {
            if (value is not JsonObject obj || !obj.TryGetPropertyValue(segment, out var next)) return new Lookup(true, null);
            value = next;
        }

        return new Lookup(false, value);
    }

    private static bool Matches(Lookup value, string op, JsonNode? operand) => op switch
    {
        "$eq" => EqualsValue(value, operand),
        "$ne" => !EqualsValue(value, operand),
        "$in" => Candidates(operand).Any(candidate => EqualsValue(value, candidate)),
        "$nin" => !Candidates(operand).Any(candidate => EqualsValue(value, candidate)),
        _ => Compares(value, op, operand),
    };

    private static IEnumerable<JsonNode?> Candidates(JsonNode? operand) =>
        operand is JsonArray array ? array : new[] { operand };

    /// <summary>Equality as MongoDB has it: an array field matches when it holds the value, numbers by value, a missing field equals only null.</summary>
    private static bool EqualsValue(Lookup value, JsonNode? operand)
    {
        if (value.Missing) return operand is null;
        if (value.Value is JsonArray array && operand is not JsonArray)
        {
            return array.Any(item => Same(item, operand));
        }

        return Same(value.Value, operand);
    }

    private static bool Same(JsonNode? a, JsonNode? b)
    {
        if (Number(a) is { } x && Number(b) is { } y) return x == y;
        if (a is null || b is null) return a is null && b is null;
        return JsonNode.DeepEquals(a, b);
    }

    /// <summary>$gt, $gte, $lt, $lte: numbers with numbers, strings with strings. Anything else, a missing field included, does not match.</summary>
    private static bool Compares(Lookup value, string op, JsonNode? operand)
    {
        if (value.Missing) return false;
        int order;
        if (Number(value.Value) is { } x && Number(operand) is { } y) order = x.CompareTo(y);
        else if (Text(value.Value) is { } s && Text(operand) is { } t) order = Math.Sign(string.CompareOrdinal(s, t));
        else return false;

        return op switch
        {
            "$gt" => order > 0,
            "$gte" => order >= 0,
            "$lt" => order < 0,
            _ => order <= 0,
        };
    }

    /// <summary>Numbers by value, then strings, then everything else.</summary>
    private static int CompareForSort(JsonNode? a, JsonNode? b)
    {
        static int Rank(JsonNode? v) => Number(v) is not null ? 0 : Text(v) is not null ? 1 : 2;
        var (ra, rb) = (Rank(a), Rank(b));
        if (ra != rb) return ra.CompareTo(rb);
        return ra switch
        {
            0 => Number(a)!.Value.CompareTo(Number(b)!.Value),
            1 => Math.Sign(string.CompareOrdinal(Text(a), Text(b))),
            _ => 0,
        };
    }

    private static double? Number(JsonNode? node) => PluginJson.Number(node);

    private static string? Text(JsonNode? node) =>
        node is JsonValue v && v.GetValueKind() == JsonValueKind.String && v.TryGetValue<string>(out var s) ? s : null;
}
