using Escalated.Services;
using Microsoft.AspNetCore.Mvc;
using Escalated.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace Escalated.Controllers.Agent;

/// <summary>
/// Agent-facing endpoints for managing live chat sessions.
/// </summary>
[ApiController]
[Route("support/agent/chat")]
[Authorize(Policy = EscalatedPolicies.Agent)]
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
    public async Task<IActionResult> ActiveSessions()
    {
        if (this.CurrentUserId() is not { } agentId) return Unauthorized();

        var sessions = await _chatService.GetActiveSessionsForAgentAsync(agentId);
        return Ok(sessions);
    }

    /// <summary>
    /// Accept a waiting chat session.
    /// </summary>
    [HttpPost("{sessionId:int}/accept")]
    public async Task<IActionResult> Accept(int sessionId)
    {
        if (this.CurrentUserId() is not { } agentId) return Unauthorized();

        try
        {
            var session = await _chatService.AcceptAsync(sessionId, agentId);
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
        if (this.CurrentUserId() is not { } agentId) return Unauthorized();

        try
        {
            var reply = await _chatService.SendMessageAsync(sessionId, request.Body, agentId, "agent");
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
    public async Task<IActionResult> End(int sessionId)
    {
        if (this.CurrentUserId() is not { } agentId) return Unauthorized();

        try
        {
            var session = await _chatService.EndAsync(sessionId, agentId);
            return Ok(session);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Transfer a chat to another agent and/or department (escalated-laravel
    /// <c>escalated.admin.chat.transfer</c>). The body is
    /// <c>{agent_id, department_id}</c> (either or both); the shared
    /// ChatActionBar's <c>{type: "agent"|"department", id}</c> is read too.
    /// </summary>
    [HttpPost("{sessionId:int}/transfer")]
    public async Task<IActionResult> Transfer(int sessionId, [FromBody] System.Text.Json.JsonElement? body)
    {
        if (await _chatService.FindByIdAsync(sessionId) is null) return NotFound();

        var agent = Field(body, "agent_id");
        var department = Field(body, "department_id");
        var type = Field(body, "type");
        if (type is { ValueKind: System.Text.Json.JsonValueKind.String } && type.Value.GetString() is { } kind)
        {
            if (kind == "agent" && Blank(agent)) agent = Field(body, "id");
            else if (kind == "department" && Blank(department)) department = Field(body, "id");
        }

        string? agentId = null;
        if (!Blank(agent))
        {
            agentId = agent!.Value.ValueKind switch
            {
                System.Text.Json.JsonValueKind.String => agent.Value.GetString()!.Trim(),
                System.Text.Json.JsonValueKind.Number => agent.Value.GetRawText(),
                _ => null,
            };
            if (agentId is null) return Unprocessable("The agent_id is invalid.");
        }

        int? departmentId = null;
        if (!Blank(department))
        {
            var raw = department!.Value.ValueKind switch
            {
                System.Text.Json.JsonValueKind.String => department.Value.GetString()!.Trim(),
                System.Text.Json.JsonValueKind.Number => department.Value.GetRawText(),
                _ => null,
            };
            if (raw is null || !int.TryParse(raw, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsed) || parsed < 1)
                return Unprocessable("The department_id must be an integer.");
            departmentId = parsed;
        }

        if (agentId is null && departmentId is null) return Unprocessable("Provide an agent_id or department_id.");

        try
        {
            var session = await _chatService.TransferAsync(sessionId, agentId, departmentId);
            return Ok(new { message = "Chat transferred.", session });
        }
        catch (ChatTransferRefusedException ex)
        {
            return Unprocessable(ex.Message);
        }
    }

    private static System.Text.Json.JsonElement? Field(System.Text.Json.JsonElement? body, string name) =>
        body is { ValueKind: System.Text.Json.JsonValueKind.Object } obj && obj.TryGetProperty(name, out var value) ? value : null;

    private static bool Blank(System.Text.Json.JsonElement? value) =>
        value is null
        || value.Value.ValueKind is System.Text.Json.JsonValueKind.Null or System.Text.Json.JsonValueKind.Undefined
        || (value.Value.ValueKind == System.Text.Json.JsonValueKind.String && string.IsNullOrWhiteSpace(value.Value.GetString()));

    private UnprocessableEntityObjectResult Unprocessable(string message) => UnprocessableEntity(new { message });

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

public record ChatMessageRequest(string Body);
