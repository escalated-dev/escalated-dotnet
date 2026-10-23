using Escalated.Authorization;
using Escalated.Configuration;
using Escalated.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Escalated.Controllers.Ui;

/// <summary>
/// The chat action the ticket page posts to, as escalated-laravel's
/// <c>escalated.admin.chat.end</c>: <c>POST {prefix}/admin/chat/{session}/end</c>
/// with the chat session's id and an empty JSON body, from the agent's signed-in
/// browser session. Any staff member may end a chat, as with
/// <c>POST support/agent/chat/{sessionId}/end</c>, which stays for API clients.
/// The route follows <see cref="EscalatedOptions.RoutePrefix"/>.
/// </summary>
[Route("support/admin/chat")]
[EscalatedPrefixedRoute]
[Authorize(Policy = EscalatedPolicies.Agent)]
public class AdminChatPageController : ControllerBase
{
    private readonly ChatSessionService _chats;

    public AdminChatPageController(ChatSessionService chats)
    {
        _chats = chats;
    }

    /// <summary>
    /// Ends the chat and resolves its ticket: <c>200 {message: "Chat ended."}</c>,
    /// or <c>422 {message: "Chat is already ended."}</c> as Laravel answers.
    /// </summary>
    [HttpPost("{session:int}/end")]
    public async Task<IActionResult> End(int session, CancellationToken ct)
    {
        var chat = await _chats.FindByIdAsync(session, ct);
        if (chat is null) return NotFound(new { message = "Chat session not found." });
        if (chat.Status == "ended")
        {
            return UnprocessableEntity(new { message = "Chat is already ended." });
        }

        await _chats.EndAsync(chat.Id, this.CurrentUserId(), ct);
        return Ok(new { message = "Chat ended." });
    }
}
