using System.Text.RegularExpressions;
using Escalated.Authorization;
using Escalated.Configuration;
using Escalated.Data;
using Escalated.Models;
using Escalated.Services;
using Escalated.Ui;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Escalated.Controllers.Ui;

/// <summary>
/// The agent panel's own endpoints (escalated-developer-context ADR
/// 2026-11-05-agent-panel-data): the agent's status, knowledge-base search for the reply
/// composer, the agent's read markers and their in-app notifications. JSON, except that an
/// Inertia visit to the status and read-marker routes goes back.
/// </summary>
[Route("support/agent")]
[Authorize(Policy = EscalatedPolicies.Agent)]
[EscalatedPage]
public class AgentPanelController : ControllerBase
{
    private const int KbDefaultLimit = 10;
    private const int KbMaxLimit = 20;
    private const int ExcerptLength = 160;

    private readonly AgentPanelService _panel;
    private readonly EscalatedDbContext _db;
    private readonly EscalatedOptions _options;

    public AgentPanelController(AgentPanelService panel, EscalatedDbContext db, IOptions<EscalatedOptions> options)
    {
        _panel = panel;
        _db = db;
        _options = options.Value;
    }

    private string Prefix => "/" + _options.RoutePrefix.Trim('/');

    private static bool IsInertia(HttpRequest request) => request.Headers.ContainsKey("X-Inertia");

    /// <summary>
    /// <c>escalated.agent.status</c>: sets the agent's status (online, away or offline).
    /// <c>{agent_status}</c>, or back for an Inertia visit; anything else is a 422.
    /// </summary>
    [HttpPut("status")]
    [HttpPost("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var input = await UiInput.ReadAsync(Request, ct);
        var status = input.String("status");
        var userId = this.CurrentUserId();
        if (userId is null) return Forbid();
        if (status is null || !AgentPanelService.Statuses.Contains(status))
        {
            const string message = "The selected status is invalid.";
            if (IsInertia(Request))
            {
                return UiResults.Back(HttpContext, $"{Prefix}/agent/tickets", errors: new Dictionary<string, string> { ["status"] = message });
            }

            return UnprocessableEntity(new { message, errors = new Dictionary<string, string[]> { ["status"] = new[] { message } } });
        }

        await _panel.SetStatusAsync(userId, status, ct);
        if (IsInertia(Request)) return UiResults.Back(HttpContext, $"{Prefix}/agent/tickets");
        return Ok(new { message = "Status updated.", agent_status = status });
    }

    /// <summary>
    /// <c>escalated.agent.kb.search</c>: published articles whose title or body contains
    /// <c>q</c> (2 to 200 characters), title matches first; <c>limit</c> 1 to 20, default 10.
    /// 404 while the knowledge base is off.
    /// </summary>
    [HttpGet("kb/search")]
    public async Task<IActionResult> KbSearch([FromQuery] string? q, [FromQuery] string? limit, CancellationToken ct)
    {
        if (!_options.KnowledgeBase.Enabled) return NotFound(new { message = "Knowledge base is not enabled." });

        var term = (q ?? string.Empty).Trim();
        var errors = new Dictionary<string, string[]>();
        if (term.Length < 2) errors["q"] = new[] { "The q field must be at least 2 characters." };
        else if (term.Length > 200) errors["q"] = new[] { "The q field must not be greater than 200 characters." };
        var take = KbDefaultLimit;
        if (!string.IsNullOrWhiteSpace(limit) && (!int.TryParse(limit, out take) || take < 1 || take > KbMaxLimit))
        {
            errors["limit"] = new[] { "The limit field must be between 1 and 20." };
        }

        if (errors.Count > 0) return UnprocessableEntity(new { message = errors.First().Value[0], errors });

        var data = (await _panel.SearchArticlesAsync(term, take, ct)).Select(article => new Dictionary<string, object?>
        {
            ["id"] = article.Id,
            ["title"] = article.Title,
            ["excerpt"] = Excerpt(article.Body),
            ["url"] = $"{Request.Scheme}://{Request.Host}{Prefix}/kb/{Uri.EscapeDataString(article.Slug)}",
            ["category"] = article.Category is null ? null : new Dictionary<string, object?>
            {
                ["id"] = article.Category.Id,
                ["name"] = article.Category.Name,
            },
            ["updated_at"] = DateTime.SpecifyKind(article.UpdatedAt, DateTimeKind.Utc),
        }).ToList();
        return Ok(new { data });
    }

    /// <summary>The article body as plain text, cut at about 160 characters.</summary>
    internal static string Excerpt(string? body)
    {
        var text = Regex.Replace(Regex.Replace(body ?? string.Empty, "<[^>]*>", " "), "\\s+", " ").Trim();
        return text.Length <= ExcerptLength ? text : text[..ExcerptLength].TrimEnd() + "...";
    }

    /// <summary><c>escalated.agent.tickets.read</c>: <c>{is_unread: false}</c>; an Inertia visit goes back.</summary>
    [HttpPost("tickets/" + TicketPageControllerBase.Reference + "/read")]
    public Task<IActionResult> MarkRead(string reference, CancellationToken ct) => MarkAsync(reference, false, ct);

    /// <summary><c>escalated.agent.tickets.unread</c>: <c>{is_unread: true}</c>; an Inertia visit goes back.</summary>
    [HttpPost("tickets/" + TicketPageControllerBase.Reference + "/unread")]
    public Task<IActionResult> MarkUnread(string reference, CancellationToken ct) => MarkAsync(reference, true, ct);

    private async Task<IActionResult> MarkAsync(string reference, bool unread, CancellationToken ct)
    {
        var ticket = await _db.Tickets.AsNoTracking().FirstOrDefaultAsync(t => t.Reference == reference, ct);
        if (ticket is null) return NotFound(new { error = "Ticket not found." });
        var userId = this.CurrentUserId();
        if (unread) await _panel.MarkUnreadAsync(userId, ticket.Id, ct);
        else await _panel.MarkReadAsync(userId, ticket.Id, ct);

        if (IsInertia(Request)) return UiResults.Back(HttpContext, $"{Prefix}/agent/tickets/{Uri.EscapeDataString(ticket.Reference)}");
        return Ok(new { is_unread = unread });
    }

    /// <summary><c>escalated.agent.notifications.index</c>: <c>{data, meta}</c>, newest first.</summary>
    [HttpGet("notifications")]
    public async Task<IActionResult> Notifications([FromQuery] string? page, [FromQuery(Name = "per_page")] string? perPage,
        [FromQuery] string? unread, CancellationToken ct)
    {
        var userId = this.CurrentUserId();
        if (!_panel.NotificationsEnabled || userId is null) return NotFound();
        var size = Math.Clamp(int.TryParse(perPage, out var pp) ? pp : 20, 1, 50);
        var number = Math.Max(1, int.TryParse(page, out var p) ? p : 1);
        var unreadOnly = unread is "1" || string.Equals(unread, "true", StringComparison.OrdinalIgnoreCase);
        return Ok(await _panel.PageAsync(userId, number, size, unreadOnly, ct));
    }

    /// <summary><c>escalated.agent.notifications.read</c>: <c>{unread_count}</c>; another agent's is a 404.</summary>
    [HttpPost("notifications/{id}/read")]
    public async Task<IActionResult> ReadNotification(string id, CancellationToken ct)
    {
        var userId = this.CurrentUserId();
        if (!_panel.NotificationsEnabled || userId is null || !await _panel.ReadAsync(userId, id, ct)) return NotFound();
        return Ok(new { unread_count = await _panel.UnreadCountAsync(userId, ct) });
    }

    /// <summary><c>escalated.agent.notifications.read-all</c>: <c>{unread_count: 0}</c>.</summary>
    [HttpPost("notifications/read-all")]
    public async Task<IActionResult> ReadAll(CancellationToken ct)
    {
        var userId = this.CurrentUserId();
        if (!_panel.NotificationsEnabled || userId is null) return NotFound();
        await _panel.ReadAllAsync(userId, ct);
        return Ok(new { unread_count = 0 });
    }
}

/// <summary>
/// Records that the customer is on the site, for the agent pages' <c>requester_online</c>
/// (ADR 2026-11-05-agent-panel-data). Staff browsing the customer portal are not recorded,
/// and presence never fails the request.
/// </summary>
internal static class PanelPresence
{
    public static async Task SeeCustomerAsync(HttpContext context, CancellationToken ct)
    {
        if (context.RequestServices.GetService<AgentPanelService>() is not { } panel) return;
        var authorization = context.RequestServices.GetService<IAuthorizationService>();
        if (authorization is not null && (await authorization.AuthorizeAsync(context.User, EscalatedPolicies.Agent)).Succeeded) return;
        var options = context.RequestServices.GetService<IOptions<EscalatedOptions>>()?.Value;
        var userId = options is null ? null : EscalatedUser.ResolveId(context.User, options);
        await panel.SeeUserAsync(userId, ct);
    }

    public static async Task SeeTicketCustomerAsync(HttpContext context, Ticket ticket, CancellationToken ct)
    {
        if (context.RequestServices.GetService<AgentPanelService>() is { } panel) await panel.SeeTicketCustomerAsync(ticket, ct);
    }
}
