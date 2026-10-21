using System.Text.Json;
using System.Text.Json.Serialization;
using Escalated.Services;
using Microsoft.AspNetCore.Mvc;
using Escalated.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace Escalated.Controllers.Admin;

[ApiController]
[Route("support/admin/saved-views")]
[Authorize(Policy = EscalatedPolicies.Admin)]
public class AdminSavedViewController : ControllerBase
{
    private readonly SavedViewService _service;

    public AdminSavedViewController(SavedViewService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        if (this.CurrentUserId() is not { } userId) return Unauthorized();

        var views = await _service.GetForUserAsync(userId);
        return Ok(views);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSavedViewRequest request)
    {
        var view = await _service.CreateAsync(request.Name, request.Filters, this.CurrentUserId(),
            request.SharedSnake ?? request.IsShared, request.SortBy, request.SortDir);
        return Ok(view);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateSavedViewRequest request)
    {
        var existing = await _service.FindAsync(id);
        if (existing == null) return NotFound();
        if (!MayChange(existing)) return Forbid();

        var view = await _service.UpdateAsync(id, request.Name, request.Filters, request.SharedSnake ?? request.IsShared);
        return Ok(view);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var existing = await _service.FindAsync(id);
        if (existing == null) return NotFound();
        if (!MayChange(existing)) return Forbid();

        await _service.DeleteAsync(id);
        return NoContent();
    }

    /// <summary>
    /// A view with no owner may be changed by any admin; otherwise only by its owner.
    /// Sharing lets other admins use a view, not change it. Mirrors the Laravel
    /// reference's <c>SavedViewController</c>.
    /// </summary>
    private bool MayChange(Escalated.Models.SavedView view) =>
        view.UserId is null || view.UserId == this.CurrentUserId();
}

/// <summary>
/// <c>filters</c> may be a JSON object (the shared ticket pages send one) or the
/// object as a string; <c>is_shared</c> is read alongside <c>isShared</c>.
/// </summary>
public record CreateSavedViewRequest(string Name,
    [property: JsonConverter(typeof(JsonTextOrObjectConverter))] string Filters,
    bool IsShared = false, string? SortBy = null, string? SortDir = null)
{
    [JsonPropertyName("is_shared")]
    public bool? SharedSnake { get; init; }
}

public record UpdateSavedViewRequest(string? Name = null,
    [property: JsonConverter(typeof(JsonTextOrObjectConverter))] string? Filters = null,
    bool? IsShared = null)
{
    [JsonPropertyName("is_shared")]
    public bool? SharedSnake { get; init; }
}

/// <summary>Reads a string as itself and any other JSON value as its JSON text.</summary>
public sealed class JsonTextOrObjectConverter : JsonConverter<string?>
{
    public override bool HandleNull => true;

    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.String:
                return reader.GetString();
            default:
                using (var document = JsonDocument.ParseValue(ref reader))
                {
                    return document.RootElement.GetRawText();
                }
        }
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}
