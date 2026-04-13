using Escalated.Enums;
using Escalated.Extensions;
using Escalated.Models;
using Escalated.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Escalated.Data;

namespace Escalated.Controllers.Admin;

[ApiController]
[Route("support/admin/tickets")]
public class AdminTicketController : ControllerBase
{
    private readonly TicketService _ticketService;
    private readonly AssignmentService _assignmentService;
    private readonly MacroService _macroService;
    private readonly TicketSplitService _splitService;
    private readonly TicketMergeService _mergeService;
    private readonly TicketSnoozeService _snoozeService;
    private readonly EscalatedDbContext _db;

    public AdminTicketController(TicketService ticketService, AssignmentService assignmentService,
        MacroService macroService, TicketSplitService splitService, TicketMergeService mergeService,
        TicketSnoozeService snoozeService, EscalatedDbContext db)
    {
        _ticketService = ticketService;
        _assignmentService = assignmentService;
        _macroService = macroService;
        _splitService = splitService;
        _mergeService = mergeService;
        _snoozeService = snoozeService;
        _db = db;
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
            .Include(t => t.SideConversations)
            .Include(t => t.ChatSessions)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (ticket == null) return NotFound();
        ticket.PopulateAttachmentUrls(Request);
        ticket.PopulateComputedFields();
        return Ok(ticket);
    }

    [HttpPost("{id:int}/reply")]
    public async Task<IActionResult> Reply(int id, [FromBody] ReplyRequest request)
    {
        var ticket = await _ticketService.FindByIdAsync(id);
        if (ticket == null) return NotFound();

        var reply = await _ticketService.AddReplyAsync(ticket, request.Body, request.AuthorId, null, false);
        return Ok(reply);
    }

    [HttpPost("{id:int}/note")]
    public async Task<IActionResult> Note(int id, [FromBody] ReplyRequest request)
    {
        var ticket = await _ticketService.FindByIdAsync(id);
        if (ticket == null) return NotFound();

        var reply = await _ticketService.AddReplyAsync(ticket, request.Body, request.AuthorId, null, true);
        return Ok(reply);
    }

    [HttpPost("{id:int}/assign")]
    public async Task<IActionResult> Assign(int id, [FromBody] AssignRequest request)
    {
        var ticket = await _ticketService.FindByIdAsync(id);
        if (ticket == null) return NotFound();

        ticket = await _assignmentService.AssignAsync(ticket, request.AgentId, request.CauserId);
        return Ok(ticket);
    }

    [HttpPost("{id:int}/status")]
    public async Task<IActionResult> Status(int id, [FromBody] StatusRequest request)
    {
        var ticket = await _ticketService.FindByIdAsync(id);
        if (ticket == null) return NotFound();

        ticket = await _ticketService.ChangeStatusAsync(ticket, TicketStatusExtensions.Parse(request.Status), request.CauserId);
        return Ok(ticket);
    }

    [HttpPost("{id:int}/priority")]
    public async Task<IActionResult> Priority(int id, [FromBody] PriorityRequest request)
    {
        var ticket = await _ticketService.FindByIdAsync(id);
        if (ticket == null) return NotFound();

        ticket = await _ticketService.ChangePriorityAsync(ticket, TicketPriorityExtensions.Parse(request.Priority), request.CauserId);
        return Ok(ticket);
    }

    [HttpPost("{id:int}/tags")]
    public async Task<IActionResult> Tags(int id, [FromBody] TagsRequest request)
    {
        var ticket = await _ticketService.FindByIdAsync(id);
        if (ticket == null) return NotFound();

        if (request.Add?.Any() == true)
            await _ticketService.AddTagsAsync(ticket, request.Add, request.CauserId);
        if (request.Remove?.Any() == true)
            await _ticketService.RemoveTagsAsync(ticket, request.Remove, request.CauserId);

        return Ok(ticket);
    }

    [HttpPost("{id:int}/department")]
    public async Task<IActionResult> Department(int id, [FromBody] DepartmentRequest request)
    {
        var ticket = await _ticketService.FindByIdAsync(id);
        if (ticket == null) return NotFound();

        ticket = await _ticketService.ChangeDepartmentAsync(ticket, request.DepartmentId, request.CauserId);
        return Ok(ticket);
    }

    [HttpPost("{id:int}/macro")]
    public async Task<IActionResult> ApplyMacro(int id, [FromBody] MacroRequest request)
    {
        var ticket = await _ticketService.FindByIdAsync(id);
        if (ticket == null) return NotFound();

        var macro = await _db.Macros.FindAsync(request.MacroId);
        if (macro == null) return NotFound("Macro not found.");

        ticket = await _macroService.ApplyAsync(macro, ticket, request.CauserId);
        return Ok(ticket);
    }

    [HttpPost("{id:int}/split")]
    public async Task<IActionResult> Split(int id, [FromBody] SplitRequest request)
    {
        var ticket = await _ticketService.FindByIdAsync(id);
        if (ticket == null) return NotFound();

        var reply = await _db.Replies.FirstOrDefaultAsync(r => r.Id == request.ReplyId && r.TicketId == id);
        if (reply == null) return NotFound("Reply not found.");

        var newTicket = await _splitService.SplitAsync(ticket, reply, request.Subject);
        return Ok(newTicket);
    }

    [HttpPost("{id:int}/merge")]
    public async Task<IActionResult> Merge(int id, [FromBody] MergeRequest request)
    {
        var source = await _ticketService.FindByIdAsync(id);
        if (source == null) return NotFound("Source ticket not found.");

        var target = await _ticketService.FindByIdAsync(request.TargetTicketId);
        if (target == null) return NotFound("Target ticket not found.");

        await _mergeService.MergeAsync(source, target, request.MergedByUserId);
        return Ok(new { message = "Tickets merged successfully." });
    }

    [HttpPost("{id:int}/snooze")]
    public async Task<IActionResult> Snooze(int id, [FromBody] SnoozeRequest request)
    {
        var ticket = await _ticketService.FindByIdAsync(id);
        if (ticket == null) return NotFound();

        ticket = await _snoozeService.SnoozeAsync(ticket, request.SnoozeUntil, request.CauserId);
        return Ok(ticket);
    }

    [HttpPost("{id:int}/unsnooze")]
    public async Task<IActionResult> Unsnooze(int id, [FromQuery] int? causerId = null)
    {
        var ticket = await _ticketService.FindByIdAsync(id);
        if (ticket == null) return NotFound();

        ticket = await _snoozeService.UnsnoozeAsync(ticket, causerId);
        return Ok(ticket);
    }

    [HttpPost("{id:int}/link")]
    public async Task<IActionResult> Link(int id, [FromBody] LinkRequest request)
    {
        var ticket = await _ticketService.FindByIdAsync(id);
        if (ticket == null) return NotFound();

        var linked = await _ticketService.FindByIdAsync(request.LinkedTicketId);
        if (linked == null) return NotFound("Linked ticket not found.");

        _db.TicketLinks.Add(new TicketLink
        {
            ParentTicketId = id,
            ChildTicketId = request.LinkedTicketId,
            LinkType = request.LinkType ?? "related",
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        return Ok(new { message = "Tickets linked." });
    }
}

// Request DTOs
public record ReplyRequest(string Body, int? AuthorId = null);
public record AssignRequest(int AgentId, int? CauserId = null);
public record StatusRequest(string Status, int? CauserId = null);
public record PriorityRequest(string Priority, int? CauserId = null);
public record TagsRequest(int[]? Add = null, int[]? Remove = null, int? CauserId = null);
public record DepartmentRequest(int DepartmentId, int? CauserId = null);
public record MacroRequest(int MacroId, int CauserId);
public record SplitRequest(int ReplyId, string? Subject = null);
public record MergeRequest(int TargetTicketId, int? MergedByUserId = null);
public record SnoozeRequest(DateTime SnoozeUntil, int? CauserId = null);
public record LinkRequest(int LinkedTicketId, string? LinkType = null);
