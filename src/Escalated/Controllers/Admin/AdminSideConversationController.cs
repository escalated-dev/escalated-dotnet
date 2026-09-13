using Escalated.Services;
using Microsoft.AspNetCore.Mvc;
using Escalated.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace Escalated.Controllers.Admin;

[ApiController]
[Route("support/admin/side-conversations")]
[Authorize(Policy = EscalatedPolicies.Admin)]
public class AdminSideConversationController : ControllerBase
{
    private readonly SideConversationService _service;

    public AdminSideConversationController(SideConversationService service)
    {
        _service = service;
    }

    [HttpGet("ticket/{ticketId:int}")]
    public async Task<IActionResult> ForTicket(int ticketId)
    {
        var conversations = await _service.GetForTicketAsync(ticketId);
        return Ok(conversations);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSideConversationRequest request)
    {
        if (this.CurrentUserId() is not { } userId) return Unauthorized();

        var conversation = await _service.CreateAsync(request.TicketId, request.Subject, userId);
        return Ok(conversation);
    }

    [HttpPost("{id:int}/reply")]
    public async Task<IActionResult> Reply(int id, [FromBody] SideConversationReplyRequest request)
    {
        if (this.CurrentUserId() is not { } userId) return Unauthorized();

        var reply = await _service.AddReplyAsync(id, request.Body, userId);
        return Ok(reply);
    }

    [HttpPost("{id:int}/close")]
    public async Task<IActionResult> Close(int id)
    {
        var conversation = await _service.CloseAsync(id);
        if (conversation == null) return NotFound();
        return Ok(conversation);
    }
}

public record CreateSideConversationRequest(int TicketId, string Subject);
public record SideConversationReplyRequest(string Body);
