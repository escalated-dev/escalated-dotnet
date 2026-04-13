using Escalated.Configuration;
using Escalated.Data;
using Escalated.Enums;
using Escalated.Extensions;
using Escalated.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Escalated.Controllers.Customer;

[ApiController]
[Route("support/tickets")]
public class CustomerTicketController : ControllerBase
{
    private readonly TicketService _ticketService;
    private readonly EscalatedOptions _options;
    private readonly EscalatedDbContext _db;

    public CustomerTicketController(TicketService ticketService, IOptions<EscalatedOptions> options,
        EscalatedDbContext db)
    {
        _ticketService = ticketService;
        _options = options.Value;
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] int requesterId,
        [FromQuery] TicketListFilters? filters, [FromQuery] int page = 1, [FromQuery] int perPage = 25)
    {
        filters ??= new TicketListFilters();
        filters.RequesterId = requesterId;
        var (items, totalCount) = await _ticketService.ListAsync(filters, page, perPage);
        foreach (var ticket in items)
            ticket.PopulateComputedFields();
        return Ok(new { data = items, total = totalCount, page, perPage });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTicketRequest request)
    {
        var ticket = await _ticketService.CreateAsync(
            request.Subject,
            request.Description,
            requesterId: request.RequesterId,
            requesterType: request.RequesterType,
            priority: request.Priority != null ? TicketPriorityExtensions.Parse(request.Priority) : null,
            departmentId: request.DepartmentId,
            ticketType: request.TicketType);

        return Ok(ticket);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Show(int id, [FromQuery] int requesterId)
    {
        var ticket = await _db.Tickets
            .Include(t => t.Replies.OrderByDescending(r => r.CreatedAt))
                .ThenInclude(r => r.Attachments)
            .Include(t => t.Attachments)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (ticket == null) return NotFound();

        // Verify ownership
        if (ticket.RequesterId != requesterId)
            return Forbid();

        ticket.PopulateAttachmentUrls(Request);
        ticket.PopulateComputedFields();
        return Ok(ticket);
    }

    [HttpPost("{id:int}/reply")]
    public async Task<IActionResult> Reply(int id, [FromBody] CustomerReplyRequest request)
    {
        var ticket = await _ticketService.FindByIdAsync(id);
        if (ticket == null) return NotFound();

        if (ticket.RequesterId != request.RequesterId)
            return Forbid();

        var reply = await _ticketService.AddReplyAsync(ticket, request.Body, request.RequesterId);
        return Ok(reply);
    }

    [HttpPost("{id:int}/close")]
    public async Task<IActionResult> Close(int id, [FromQuery] int requesterId)
    {
        if (!_options.AllowCustomerClose)
            return StatusCode(403, new { error = "Customers cannot close tickets." });

        var ticket = await _ticketService.FindByIdAsync(id);
        if (ticket == null) return NotFound();
        if (ticket.RequesterId != requesterId) return Forbid();

        ticket = await _ticketService.ChangeStatusAsync(ticket, TicketStatus.Closed, requesterId);
        return Ok(ticket);
    }

    [HttpPost("{id:int}/reopen")]
    public async Task<IActionResult> Reopen(int id, [FromQuery] int requesterId)
    {
        var ticket = await _ticketService.FindByIdAsync(id);
        if (ticket == null) return NotFound();
        if (ticket.RequesterId != requesterId) return Forbid();

        ticket = await _ticketService.ChangeStatusAsync(ticket, TicketStatus.Reopened, requesterId);
        return Ok(ticket);
    }
}

public record CreateTicketRequest(string Subject, string? Description = null,
    int? RequesterId = null, string? RequesterType = null,
    string? Priority = null, int? DepartmentId = null, string? TicketType = null);

public record CustomerReplyRequest(string Body, int RequesterId);
