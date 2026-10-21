using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Escalated.Ui;

/// <summary>
/// How page props are serialized. The shared Vue pages read snake_case
/// (<c>from_name</c>, <c>created_at</c>), as Laravel's Eloquent models produce,
/// while this package's JSON API keeps ASP.NET Core's camelCase. These options
/// apply to page props only.
///
/// <para>Object properties are written snake_case. Dictionary keys are written as
/// given, which is how the top-level prop names keep the exact spelling the page
/// declares (<c>recentTickets</c>, <c>is_following</c>): <see cref="TopLevel"/>
/// turns an anonymous props object into a dictionary under its own names.</para>
/// </summary>
public static class UiJson
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DictionaryKeyPolicy = null,
            ReferenceHandler = ReferenceHandler.IgnoreCycles,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        options.Converters.Add(new UtcDateTimeConverter());
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    public static string Serialize(object? value) => JsonSerializer.Serialize(value, Options);

    /// <summary>
    /// The props object as a dictionary keyed by its own property names, unchanged.
    /// A dictionary is copied as is; null gives an empty one.
    /// </summary>
    public static Dictionary<string, object?> TopLevel(object? props)
    {
        switch (props)
        {
            case null:
                return new Dictionary<string, object?>();
            case IDictionary<string, object?> dictionary:
                return new Dictionary<string, object?>(dictionary);
            case IReadOnlyDictionary<string, object?> readOnly:
                return readOnly.ToDictionary(pair => pair.Key, pair => pair.Value);
        }

        return props.GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.GetIndexParameters().Length == 0)
            .ToDictionary(property => property.Name, property => property.GetValue(props));
    }

    /// <summary>
    /// ISO 8601 in UTC with a <c>Z</c>. EF hands back <see cref="DateTimeKind.Unspecified"/>
    /// from SQLite, and without the zone a browser would read the time as local.
    /// </summary>
    private sealed class UtcDateTimeConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            DateTime.Parse(reader.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            var utc = value.Kind switch
            {
                DateTimeKind.Local => value.ToUniversalTime(),
                DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
                _ => value,
            };
            writer.WriteStringValue(utc.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture));
        }
    }
}
