using Escalated.Services;
using Microsoft.AspNetCore.Mvc;

namespace Escalated.Controllers.Admin;

[ApiController]
[Route("support/admin/saved-views")]
public class AdminSavedViewController : ControllerBase
{
    private readonly SavedViewService _service;

    public AdminSavedViewController(SavedViewService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] int userId)
    {
        var views = await _service.GetForUserAsync(userId);
        return Ok(views);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSavedViewRequest request)
    {
        var view = await _service.CreateAsync(request.Name, request.Filters, request.UserId,
            request.IsShared, request.SortBy, request.SortDir);
        return Ok(view);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateSavedViewRequest request)
    {
        var view = await _service.UpdateAsync(id, request.Name, request.Filters, request.IsShared);
        if (view == null) return NotFound();
        return Ok(view);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var deleted = await _service.DeleteAsync(id);
        if (!deleted) return NotFound();
        return NoContent();
    }
}

public record CreateSavedViewRequest(string Name, string Filters, int? UserId = null,
    bool IsShared = false, string? SortBy = null, string? SortDir = null);
public record UpdateSavedViewRequest(string? Name = null, string? Filters = null, bool? IsShared = null);
