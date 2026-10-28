using System.Text.Json;
using System.Text.Json.Serialization;
using Escalated.Data;
using Escalated.Hubs;
using Escalated.Models;
using Escalated.Services;
using Escalated.Widget;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Controllers.Widget;

/// <summary>
/// Public widget endpoints for starting and participating in live chat sessions.
/// A chat is reached only through the unguessable token returned as <c>id</c>
/// when it starts; numeric session ids are never accepted here, so one visitor
/// cannot read or act on another's chat.
/// </summary>
[ApiController]
[Route("support/widget/chat")]
[AllowAnonymous]
[WidgetEnabled]
public class WidgetChatController : ControllerBase
{
    private const int MaxMessageLength = 5000;
    private const int TranscriptLimit = 100;

    private readonly ChatSessionService _chatService;
    private readonly ChatAvailabilityService _availability;
    private readonly EscalatedDbContext _db;
    private readonly IUserDirectory _users;

    public WidgetChatController(
        ChatSessionService chatService,
        ChatAvailabilityService availability,
        EscalatedDbContext db,
        IUserDirectory users)
    {
        _chatService = chatService;
        _availability = availability;
        _db = db;
        _users = users;
    }

    /// <summary>
    /// Check whether live chat is currently available.
    /// </summary>
    [HttpGet("availability")]
    public async Task<IActionResult> Availability(
        [FromQuery] int? departmentId,
        [FromQuery(Name = "department_id")] int? widgetDepartmentId)
    {
        var department = widgetDepartmentId ?? departmentId;
        var available = await _availability.IsAvailableAsync(department);
        var queueDepth = await _availability.GetQueueDepthAsync(department);
        return Ok(new { available, queueDepth });
    }

    /// <summary>
    /// Start a new chat session from the widget. The response's <c>id</c> is the
    /// visitor's token for every other chat route.
    /// </summary>
    [HttpPost("start")]
    [GuestThrottle(GuestThrottleScope.Ticket)]
    public async Task<IActionResult> Start([FromBody] StartChatRequest request, [FromServices] GuestAccessService access)
    {
        var invalid = WidgetMetadata.Validate(request.Metadata);
        if (invalid is not null)
        {
            ModelState.AddModelError("metadata", invalid);
            return ValidationProblem(ModelState);
        }

        var departmentId = request.WidgetDepartmentId ?? request.DepartmentId;
        if (!await WidgetDepartments.IsAllowedAsync(_db, departmentId, HttpContext.RequestAborted))
        {
            ModelState.AddModelError(WidgetDepartments.Field, WidgetDepartments.Invalid);
            return ValidationProblem(ModelState);
        }

        // The visitor proves the email with a `chat` code, as for a ticket.
        if (GuestAccessService.NormalizeEmail(request.Email) is null)
            return GuestAccessEndpoints.Invalid(this, new Dictionary<string, string[]> { ["email"] = new[] { "A valid email address is required." } });
        if (string.IsNullOrEmpty(request.VerificationId) || string.IsNullOrEmpty(request.VerificationCode)
            || !await access.ConsumeAsync(request.VerificationId, request.VerificationCode, request.Email, "chat", HttpContext.RequestAborted))
            return GuestAccessEndpoints.InvalidCode(this);

        try
        {
            var session = await _chatService.StartAsync(
                request.Name ?? "Visitor",
                request.Email,
                request.Message,
                departmentId,
                metadata: WidgetMetadata.Sanitize(request.Metadata, HttpContext.Items[WidgetOrigins.ItemKey] as string));
            var token = await _chatService.IssueVisitorTokenAsync(session);
            var reference = await _db.Tickets.Where(t => t.Id == session.TicketId).Select(t => t.Reference).SingleAsync();
            var agentName = string.IsNullOrEmpty(session.AgentId)
                ? null
                : await StaffNameAsync(session.AgentId, new Dictionary<string, string>(), HttpContext.RequestAborted);

            // As in escalated-laravel: 201, and `id` and `session_id` are both
            // the chat token; the numeric session and ticket ids stay internal.
            return StatusCode(StatusCodes.Status201Created, new
            {
                id = token,
                session_id = token,
                // The token stops working after this; the visitor starts a new chat.
                expires_at = session.VisitorTokenExpiresAt,
                ticket_reference = reference,
                status = session.Status,
                agent_name = agentName,
                agent = agentName is null ? null : new { name = agentName },
            });
        }
        catch (Exception ex)
        {
            await access.ReleaseAsync(request.VerificationId!, CancellationToken.None);
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// The transcript the widget polls: public replies, oldest first.
    /// </summary>
    [HttpGet("{token}/messages")]
    public async Task<IActionResult> Messages(string token, CancellationToken ct)
    {
        var session = await _chatService.FindByVisitorTokenAsync(token, ct);
        if (session == null) return ChatNotFound();

        var replies = await _db.Replies
            .Where(r => r.TicketId == session.TicketId && !r.IsInternalNote && (r.Type == null || r.Type != "note"))
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .Take(TranscriptLimit)
            .ToListAsync(ct);
        replies.Reverse();

        var names = new Dictionary<string, string>();
        var messages = new List<object>();
        foreach (var reply in replies)
        {
            var isAgent = IsStaff(reply);
            messages.Add(new
            {
                id = reply.Id,
                body = reply.Body,
                is_agent = isAgent,
                author = new { name = isAgent ? await StaffNameAsync(reply.AuthorId!, names, ct) : session.VisitorName },
                created_at = reply.CreatedAt,
            });
        }

        return Ok(new
        {
            messages,
            // Agents type over SignalR; nothing is stored to report here.
            typing = (string?)null,
            agent = await AgentAsync(session, names, ct),
            ended = session.Status == "ended",
        });
    }

    /// <summary>
    /// Send a visitor message in the chat.
    /// </summary>
    [HttpPost("{token}/messages")]
    public async Task<IActionResult> SendMessage(string token, [FromBody] VisitorMessageRequest request)
    {
        var session = await _chatService.FindByVisitorTokenAsync(token);
        if (session == null) return ChatNotFound();
        if (session.Status == "ended") return FieldError("chat", "Chat has ended.");

        var body = request.Body?.Trim();
        if (string.IsNullOrEmpty(body)) return FieldError("body", "The message is required.");
        if (body.Length > MaxMessageLength)
            return FieldError("body", $"The message may not be longer than {MaxMessageLength} characters.");

        try
        {
            var reply = await _chatService.SendMessageAsync(session.Id, body, authorType: "visitor");
            return Ok(new { reply.Id, reply.Body, reply.CreatedAt });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// The visitor is typing; tell agents watching the chat.
    /// </summary>
    [HttpPost("{token}/typing")]
    public async Task<IActionResult> Typing(string token, CancellationToken ct)
    {
        var session = await _chatService.FindByVisitorTokenAsync(token, ct);
        if (session == null) return ChatNotFound();
        if (session.Status == "ended") return FieldError("chat", "Chat has ended.");

        var broadcasts = HttpContext.RequestServices.GetService(typeof(TicketBroadcastService)) as TicketBroadcastService;
        if (broadcasts != null)
        {
            await broadcasts.SendAsync(session.TicketId, $"chat-{session.Id}", "ChatTyping",
                new { sessionId = session.Id, userName = session.VisitorName }, ct: ct);
        }

        return Ok(new { message = "Typing updated." });
    }

    /// <summary>
    /// End the chat from the visitor side.
    /// </summary>
    [HttpPost("{token}/end")]
    public async Task<IActionResult> End(string token)
    {
        var session = await _chatService.FindByVisitorTokenAsync(token);
        if (session == null) return ChatNotFound();
        if (session.Status == "ended") return FieldError("chat", "Chat has already ended.");

        try
        {
            var ended = await _chatService.EndAsync(session.Id);
            return Ok(new { id = token, ended.Status, ended.EndedAt });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Rate an ended chat, once.
    /// </summary>
    [HttpPost("{token}/rate")]
    public async Task<IActionResult> Rate(string token, [FromBody] ChatRateRequest request, CancellationToken ct)
    {
        var session = await _chatService.FindByVisitorTokenAsync(token, ct);
        if (session == null) return ChatNotFound();
        if (session.Status != "ended") return FieldError("rating", "Chat must be ended before rating.");
        if (request.Rating is not (>= 1 and <= 5)) return FieldError("rating", "The rating must be a whole number from 1 to 5.");
        if (await _db.SatisfactionRatings.AnyAsync(r => r.TicketId == session.TicketId, ct))
            return FieldError("rating", "This chat has already been rated.");

        var comment = request.Comment?.Trim();
        _db.SatisfactionRatings.Add(new SatisfactionRating
        {
            TicketId = session.TicketId,
            Rating = request.Rating.Value,
            Comment = string.IsNullOrEmpty(comment) ? null : comment.Length > 1000 ? comment[..1000] : comment,
            CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync(ct);

        return Ok(new { message = "Rating submitted." });
    }

    /// <summary>Replies with an author other than the visitor are staff.</summary>
    private static bool IsStaff(Reply reply) =>
        !string.IsNullOrEmpty(reply.AuthorId) && reply.AuthorType != "visitor";

    private async Task<object?> AgentAsync(ChatSession session, Dictionary<string, string>? names = null, CancellationToken ct = default) =>
        string.IsNullOrEmpty(session.AgentId)
            ? null
            : new { name = await StaffNameAsync(session.AgentId, names ?? new Dictionary<string, string>(), ct) };

    private async Task<string> StaffNameAsync(string userId, Dictionary<string, string> names, CancellationToken ct)
    {
        if (!names.TryGetValue(userId, out var name))
        {
            var user = await _users.FindAsync(userId, ct);
            name = string.IsNullOrWhiteSpace(user?.Name) ? "Agent" : user.Name!;
            names[userId] = name;
        }

        return name;
    }

    private NotFoundObjectResult ChatNotFound() => NotFound(new { message = "Chat session not found." });

    private ActionResult FieldError(string field, string message)
    {
        ModelState.AddModelError(field, message);
        return ValidationProblem(ModelState);
    }
}

/// <param name="Metadata">Optional host parameters; see <see cref="WidgetMetadata"/>.</param>
public record StartChatRequest(string? Name, string? Email, string? Message, int? DepartmentId, JsonElement? Metadata = null)
{
    /// <summary>The shared widget's <c>department_id</c>; wins over <c>departmentId</c>.</summary>
    [JsonPropertyName("department_id")]
    public int? WidgetDepartmentId { get; init; }

    [JsonPropertyName("verification_id")]
    public string? VerificationId { get; init; }

    [JsonPropertyName("verification_code")]
    public string? VerificationCode { get; init; }
}

public record VisitorMessageRequest(string? Body);

public record ChatRateRequest(int? Rating, string? Comment = null);
