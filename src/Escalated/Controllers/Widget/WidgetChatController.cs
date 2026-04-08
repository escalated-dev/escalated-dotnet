using Escalated.Services;
using Microsoft.AspNetCore.Mvc;

namespace Escalated.Controllers.Widget;

/// <summary>
/// Public widget endpoints for starting and participating in live chat sessions.
/// </summary>
[ApiController]
[Route("support/widget/chat")]
public class WidgetChatController : ControllerBase
{
    private readonly ChatSessionService _chatService;
    private readonly ChatAvailabilityService _availability;

    public WidgetChatController(ChatSessionService chatService, ChatAvailabilityService availability)
    {
        _chatService = chatService;
        _availability = availability;
    }

    /// <summary>
    /// Check whether live chat is currently available.
    /// </summary>
    [HttpGet("availability")]
    public async Task<IActionResult> Availability([FromQuery] int? departmentId)
    {
        var available = await _availability.IsAvailableAsync(departmentId);
        var queueDepth = await _availability.GetQueueDepthAsync(departmentId);
        return Ok(new { available, queueDepth });
    }

    /// <summary>
    /// Start a new chat session from the widget.
    /// </summary>
    [HttpPost("start")]
    public async Task<IActionResult> Start([FromBody] StartChatRequest request)
    {
        try
        {
            var session = await _chatService.StartAsync(
                request.Name ?? "Visitor",
                request.Email,
                request.Message,
                request.DepartmentId);

            return Ok(new
            {
                session.Id,
                session.TicketId,
                session.Status,
                session.VisitorName,
                session.CreatedAt
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Send a visitor message in an existing chat session.
    /// </summary>
    [HttpPost("{sessionId:int}/messages")]
    public async Task<IActionResult> SendMessage(int sessionId, [FromBody] VisitorMessageRequest request)
    {
        try
        {
            var reply = await _chatService.SendMessageAsync(sessionId, request.Body, authorType: "visitor");
            return Ok(new { reply.Id, reply.Body, reply.CreatedAt });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// End a chat session from the visitor side.
    /// </summary>
    [HttpPost("{sessionId:int}/end")]
    public async Task<IActionResult> End(int sessionId)
    {
        try
        {
            var session = await _chatService.EndAsync(sessionId);
            return Ok(new { session.Id, session.Status, session.EndedAt });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}

public record StartChatRequest(string? Name, string? Email, string? Message, int? DepartmentId);
public record VisitorMessageRequest(string Body);
