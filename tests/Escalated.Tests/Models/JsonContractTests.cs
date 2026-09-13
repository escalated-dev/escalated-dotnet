using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Escalated.Data;
using Xunit;

namespace Escalated.Tests.Models;

/// <summary>
/// Controllers return entities, and MVC serializes them with the web defaults:
/// camelCase names. A property named <c>Subjects</c> becomes <c>subjects</c>, and
/// if another property is explicitly named <c>subjects</c> the serializer refuses
/// the whole type, at the first response that contains one.
///
/// <para>The controller tests call actions directly and inspect the result object,
/// so none of them ever serialized one.</para>
/// </summary>
public class JsonContractTests
{
    public static IEnumerable<object[]> ModelTypes() =>
        typeof(EscalatedDbContext).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true, ContainsGenericParameters: false })
            .Where(t => t.Namespace?.StartsWith("Escalated.Models", StringComparison.Ordinal) == true)
            .OrderBy(t => t.FullName)
            .Select(t => new object[] { t.FullName! });

    [Theory]
    [MemberData(nameof(ModelTypes))]
    public void ModelSerializesWithMvcDefaults(string typeName)
    {
        var type = typeof(EscalatedDbContext).Assembly.GetType(typeName, throwOnError: true)!;
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };

        var exception = Record.Exception(() => options.GetTypeInfo(type));

        Assert.True(exception is null, $"{typeName}: {exception?.Message}");
    }
}
