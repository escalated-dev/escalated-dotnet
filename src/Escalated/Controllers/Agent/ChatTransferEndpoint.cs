using Escalated.Services;
using Microsoft.AspNetCore.Mvc;

namespace Escalated.Controllers.Agent;

/// <summary>
/// Live chat transfer (escalated-laravel <c>ChatController::transfer</c>), shared by
/// the agent chat API and the <c>escalated.admin.chat.transfer</c> route the ticket
/// page posts to. The body is <c>{agent_id, department_id}</c> (either or both); the
/// shared ChatActionBar's <c>{type: "agent"|"department", id}</c> is read too.
/// 200 <c>{message, session}</c>, 422 <c>{message}</c>, or 404 for an unknown session.
/// </summary>
internal static class ChatTransferEndpoint
{
    public static async Task<IActionResult> HandleAsync(ControllerBase controller, ChatSessionService chats, int sessionId, System.Text.Json.JsonElement? body)
    {
        if (await chats.FindByIdAsync(sessionId) is null) return controller.NotFound();

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
            if (agentId is null) return Unprocessable(controller, "The agent_id is invalid.");
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
                return Unprocessable(controller, "The department_id must be an integer.");
            departmentId = parsed;
        }

        if (agentId is null && departmentId is null) return Unprocessable(controller, "Provide an agent_id or department_id.");

        try
        {
            var session = await chats.TransferAsync(sessionId, agentId, departmentId);
            return controller.Ok(new { message = "Chat transferred.", session });
        }
        catch (ChatTransferRefusedException ex)
        {
            return Unprocessable(controller, ex.Message);
        }
    }

    private static System.Text.Json.JsonElement? Field(System.Text.Json.JsonElement? body, string name) =>
        body is { ValueKind: System.Text.Json.JsonValueKind.Object } obj && obj.TryGetProperty(name, out var value) ? value : null;

    private static bool Blank(System.Text.Json.JsonElement? value) =>
        value is null
        || value.Value.ValueKind is System.Text.Json.JsonValueKind.Null or System.Text.Json.JsonValueKind.Undefined
        || (value.Value.ValueKind == System.Text.Json.JsonValueKind.String && string.IsNullOrWhiteSpace(value.Value.GetString()));

    private static UnprocessableEntityObjectResult Unprocessable(ControllerBase controller, string message) => controller.UnprocessableEntity(new { message });
}
