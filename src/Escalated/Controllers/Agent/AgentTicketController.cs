using Escalated.Configuration;
using Escalated.Data;
using Escalated.Enums;
using Escalated.Events;
using Escalated.Extensions;
using Escalated.Models;
using Escalated.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Controllers.Agent;

[ApiController]
[Route("support/agent/tickets")]
public class AgentTicketController : ControllerBase
{
    private readonly TicketService _ticketService;
    private readonly AssignmentService _assignmentService;
    private readonly MacroService _macroService;
    private readonly EscalatedDbContext _db;
    private readonly ITicketActionRegistry _actions;
    private readonly IEscalatedEventDispatcher _events;

    public AgentTicketController(TicketService ticketService, AssignmentService assignmentService,
        MacroService macroService, EscalatedDbContext db, ITicketActionRegistry actions,
        IEscalatedEventDispatcher events)
    {
        _ticketService = ticketService;
        _assignmentService = assignmentService;
        _macroService = macroService;
        _db = db;
        _actions = actions;
        _events = events;
    }

    /// <summary>Serializes the visible custom actions for a ticket, adding url + method.</summary>
    private List<Dictionary<string, object?>> CustomActionsForTicket(Ticket ticket, int? userId)
    {
        var actions = _actions.ForTicket(ticket, userId).Select(a => new Dictionary<string, object?>(a)).ToList();
        foreach (var a in actions)
        {
            a["url"] = $"/support/agent/tickets/{ticket.Id}/actions/{a["key"]}";
            a["method"] = "post";
        }
        return actions;
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] TicketListFilters? filters,
        [FromQuery] int page = 1, [FromQuery] int perPage = 25)
    {
        var (items, totalCount) = await _ticketService.ListAsync(filters, page, perPage);
        foreach (var ticket in items)
            ticket.PopulateComputedFields();
        return Ok(new { data = items, total = totalCount, page, perPage });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Show(int id)
    {
        var ticket = await _db.Tickets
            .Include(t => t.Replies.OrderByDescending(r => r.CreatedAt))
                .ThenInclude(r => r.Attachments)
            .Include(t => t.Attachments)
            .Include(t => t.Tags)
            .Include(t => t.Department)
            .Include(t => t.SlaPolicy)
            .Include(t => t.Activities.OrderByDescending(a => a.CreatedAt).Take(20))
            .Include(t => t.SatisfactionRating)
            .Include(t => t.ChatSessions)
            .Include(t => t.LinksAsParent).ThenInclude(l => l.ChildTicket)
            .Include(t => t.LinksAsChild).ThenInclude(l => l.ParentTicket)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (ticket == null) return NotFound();
        ticket.PopulateAttachmentUrls(Request);
        ticket.PopulateComputedFields();

        // Chat messages: replies that belong to the active chat session's ticket
        if (ticket.ChatSessionId != null)
        {
            ticket.ChatMessages = ticket.Replies
                .OrderBy(r => r.CreatedAt)
                .Select(r => new
                {
                    id = r.Id,
                    body = r.Body,
                    author_type = r.AuthorType,
                    author_id = r.AuthorId,
                    created_at = r.CreatedAt
                });
        }

        // Requester ticket count
        if (ticket.RequesterId != null)
        {
            ticket.RequesterTicketCount = await _db.Tickets
                .CountAsync(t => t.RequesterType == ticket.RequesterType
                              && t.RequesterId == ticket.RequesterId);
        }
        else if (ticket.GuestEmail != null)
        {
            ticket.RequesterTicketCount = await _db.Tickets
                .CountAsync(t => t.GuestEmail == ticket.GuestEmail);
        }

        ticket.CustomActions = CustomActionsForTicket(ticket, null);

        return Ok(ticket);
    }

    [HttpPost("{id:int}/actions/{action}")]
    public async Task<IActionResult> CustomAction(int id, string action,
        [FromBody] CustomActionRequest? request, CancellationToken ct)
    {
        var ticket = await _ticketService.FindByIdAsync(id);
        if (ticket == null) return NotFound();

        var config = _actions.Find(action);
        if (config == null || !config.Visible)
            return NotFound(new { error = "Custom action not found." });
        if (!config.Enabled)
            return StatusCode(403, new { error = "Custom action is not enabled." });

        var userId = request?.UserId;

        // Record an internal note for auditability.
        await _ticketService.AddReplyAsync(ticket, $"Custom action \"{action}\" was triggered.",
            userId, "system", isNote: true, ct);

        await _events.DispatchAsync(
            new TicketCustomActionTriggeredEvent(ticket, action, userId, request?.Payload, config.Metadata), ct);

        return Ok(new { message = "Custom action dispatched.", action });
    }

    [HttpPost("{id:int}/reply")]
    public async Task<IActionResult> Reply(int id, [FromBody] Admin.ReplyRequest request)
    {
        var ticket = await _ticketService.FindByIdAsync(id);
        if (ticket == null) return NotFound();

        var reply = await _ticketService.AddReplyAsync(ticket, request.Body, request.AuthorId, null, false);
        return Ok(reply);
    }

    [HttpPost("{id:int}/note")]
    public async Task<IActionResult> Note(int id, [FromBody] Admin.ReplyRequest request)
    {
        var ticket = await _ticketService.FindByIdAsync(id);
        if (ticket == null) return NotFound();

        var reply = await _ticketService.AddReplyAsync(ticket, request.Body, request.AuthorId, null, true);
        return Ok(reply);
    }

    [HttpPost("{id:int}/assign")]
    public async Task<IActionResult> Assign(int id, [FromBody] Admin.AssignRequest request)
    {
        var ticket = await _ticketService.FindByIdAsync(id);
        if (ticket == null) return NotFound();

        ticket = await _assignmentService.AssignAsync(ticket, request.AgentId, request.CauserId);
        return Ok(ticket);
    }

    [HttpPost("{id:int}/status")]
    public async Task<IActionResult> Status(int id, [FromBody] Admin.StatusRequest request)
    {
        var ticket = await _ticketService.FindByIdAsync(id);
        if (ticket == null) return NotFound();

        ticket = await _ticketService.ChangeStatusAsync(ticket, TicketStatusExtensions.Parse(request.Status), request.CauserId);
        return Ok(ticket);
    }

    [HttpPost("{id:int}/priority")]
    public async Task<IActionResult> Priority(int id, [FromBody] Admin.PriorityRequest request)
    {
        var ticket = await _ticketService.FindByIdAsync(id);
        if (ticket == null) return NotFound();

        ticket = await _ticketService.ChangePriorityAsync(ticket, TicketPriorityExtensions.Parse(request.Priority), request.CauserId);
        return Ok(ticket);
    }

    [HttpPost("{id:int}/macro")]
    public async Task<IActionResult> ApplyMacro(int id, [FromBody] Admin.MacroRequest request)
    {
        var ticket = await _ticketService.FindByIdAsync(id);
        if (ticket == null) return NotFound();

        var macro = await _db.Macros.FindAsync(request.MacroId);
        if (macro == null) return NotFound("Macro not found.");

        ticket = await _macroService.ApplyAsync(macro, ticket, request.CauserId);
        return Ok(ticket);
    }

    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard([FromQuery] int agentId)
    {
        var workload = await _assignmentService.GetAgentWorkloadAsync(agentId);
        return Ok(workload);
    }

    [HttpPost("bulk")]
    public async Task<IActionResult> BulkAction([FromBody] BulkActionRequest request)
    {
        var results = new List<object>();
        foreach (var ticketId in request.TicketIds)
        {
            var ticket = await _ticketService.FindByIdAsync(ticketId);
            if (ticket == null) continue;

            try
            {
                switch (request.Action)
                {
                    case "close":
                        await _ticketService.ChangeStatusAsync(ticket, TicketStatus.Closed, request.CauserId);
                        break;
                    case "resolve":
                        await _ticketService.ChangeStatusAsync(ticket, TicketStatus.Resolved, request.CauserId);
                        break;
                    case "assign":
                        if (request.Value != null)
                            await _assignmentService.AssignAsync(ticket, int.Parse(request.Value), request.CauserId);
                        break;
                    case "change_priority":
                        if (request.Value != null)
                            await _ticketService.ChangePriorityAsync(ticket, TicketPriorityExtensions.Parse(request.Value), request.CauserId);
                        break;
                }
                results.Add(new { ticketId, success = true });
            }
            catch (Exception ex)
            {
                results.Add(new { ticketId, success = false, error = ex.Message });
            }
        }
        return Ok(new { results });
    }
}

public record BulkActionRequest(int[] TicketIds, string Action, int? CauserId = null, string? Value = null);

public record CustomActionRequest(int? UserId = null, Dictionary<string, object>? Payload = null);
