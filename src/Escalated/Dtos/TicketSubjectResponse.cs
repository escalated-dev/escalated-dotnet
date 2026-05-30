using System.Text.Json.Serialization;

namespace Escalated.Dtos;

/// <summary>Serialized shape for <c>subjects[]</c> on ticket API responses.</summary>
public record TicketSubjectResponse(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("role")] string? Role,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("subtitle")] string? Subtitle,
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("color")] string? Color,
    [property: JsonPropertyName("icon")] string? Icon,
    [property: JsonPropertyName("missing")] bool Missing);
