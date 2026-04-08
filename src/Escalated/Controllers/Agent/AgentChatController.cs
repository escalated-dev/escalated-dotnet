using Escalated.Services;
using Microsoft.AspNetCore.Mvc;

namespace Escalated.Controllers.Agent;

/// <summary>
/// Agent-facing endpoints for managing live chat sessions.
/// </summary>
[ApiController]
[Route("support/agent/chat")]
public class AgentChatController : ControllerBase
{
    private readonly ChatSessionService _chatService;
    private readonly ChatAvailabilityService _availability;

    public AgentChatController(ChatSessionService chatService, ChatAvailabilityService availability)
    {
        _chatService = chatService;
        _availability = availability;
    }

    /// <summary>
    /// List all waiting chat sessions (queue).
    /// </summary>
    [HttpGet("queue")]
    public async Task<IActionResult> Queue()
    {
        var sessions = await _chatService.GetWaitingSessionsAsync();
        return Ok(sessions);
    }

    /// <summary>
    /// List active chat sessions for the requesting agent.
    /// </summary>
    [HttpGet("active")]
    public async Task<IActionResult> ActiveSessions([FromQuery] int agentId)
    {
        var sessions = await _chatService.GetActiveSessionsForAgentAsync(agentId);
        return Ok(sessions);
    }

    /// <summary>
    /// Accept a waiting chat session.
    /// </summary>
    [HttpPost("{sessionId:int}/accept")]
    public async Task<IActionResult> Accept(int sessionId, [FromBody] AcceptChatRequest request)
    {
        try
        {
            var session = await _chatService.AcceptAsync(sessionId, request.AgentId);
            return Ok(session);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Send a message in a chat session (as agent).
    /// </summary>
    [HttpPost("{sessionId:int}/messages")]
    public async Task<IActionResult> SendMessage(int sessionId, [FromBody] ChatMessageRequest request)
    {
        try
        {
            var reply = await _chatService.SendMessageAsync(sessionId, request.Body, request.AgentId, "agent");
            return Ok(reply);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// End a chat session.
    /// </summary>
    [HttpPost("{sessionId:int}/end")]
    public async Task<IActionResult> End(int sessionId, [FromBody] EndChatRequest request)
    {
        try
        {
            var session = await _chatService.EndAsync(sessionId, request.AgentId);
            return Ok(session);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get a specific chat session.
    /// </summary>
    [HttpGet("{sessionId:int}")]
    public async Task<IActionResult> Show(int sessionId)
    {
        var session = await _chatService.FindByIdAsync(sessionId);
        if (session == null) return NotFound();
        return Ok(session);
    }
}

public record AcceptChatRequest(int AgentId);
public record ChatMessageRequest(string Body, int AgentId);
public record EndChatRequest(int AgentId);
