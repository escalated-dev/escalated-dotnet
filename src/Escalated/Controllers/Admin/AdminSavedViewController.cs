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
            request.IsShared, request.SortBy, request.SortDir);
        return Ok(view);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateSavedViewRequest request)
    {
        var existing = await _service.FindAsync(id);
        if (existing == null) return NotFound();
        if (!MayChange(existing)) return Forbid();

        var view = await _service.UpdateAsync(id, request.Name, request.Filters, request.IsShared);
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

public record CreateSavedViewRequest(string Name, string Filters,
    bool IsShared = false, string? SortBy = null, string? SortDir = null);
public record UpdateSavedViewRequest(string? Name = null, string? Filters = null, bool? IsShared = null);
