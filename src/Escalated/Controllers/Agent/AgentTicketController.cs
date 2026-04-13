using Escalated.Data;
using Escalated.Enums;
using Escalated.Extensions;
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

    public AgentTicketController(TicketService ticketService, AssignmentService assignmentService,
        MacroService macroService, EscalatedDbContext db)
    {
        _ticketService = ticketService;
        _assignmentService = assignmentService;
        _macroService = macroService;
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] TicketListFilters? filters,
        [FromQuery] int page = 1, [FromQuery] int perPage = 25)
    {
        var (items, totalCount) = await _ticketService.ListAsync(filters, page, perPage);
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
            .FirstOrDefaultAsync(t => t.Id == id);

        if (ticket == null) return NotFound();
        ticket.PopulateAttachmentUrls(Request);
        return Ok(ticket);
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
