using Escalated.Authorization;
using Escalated.Configuration;
using Escalated.Data;
using Escalated.Enums;
using Escalated.Extensions;
using Escalated.Models;
using Escalated.Services;
using Escalated.Services.Email.Inbound;
using Escalated.Ui;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Escalated.Controllers.Ui;

/// <summary>
/// The customer pages (<c>Escalated/Customer/Index</c>, <c>Create</c>, <c>Show</c>)
/// and their form posts, with escalated-laravel's routes and props
/// (<c>Customer/TicketController</c>, <c>SatisfactionRatingController</c>,
/// <c>CustomerTicketPayload</c>). A customer sees only the tickets they opened,
/// only public replies, and staff by display name only. Only served when
/// <c>Escalated:Ui:Enabled</c> is on.
/// </summary>
[Route("support")]
[Authorize(Policy = EscalatedPolicies.Customer)]
[EscalatedPage]
public class CustomerPageController : ControllerBase
{
    private const string Reference = AgentPageController.Reference;

    private static readonly string[] Priorities = Enum.GetValues<TicketPriority>().Select(p => p.ToValue()).ToArray();
    private static readonly HashSet<string> Statuses = Enum.GetValues<TicketStatus>().Select(s => s.ToValue()).ToHashSet();

    private readonly EscalatedDbContext _db;
    private readonly TicketService _tickets;
    private readonly UiPresenter _present;
    private readonly IAttachmentStorage _storage;
    private readonly EscalatedOptions _options;

    public CustomerPageController(EscalatedDbContext db, TicketService tickets, UiPresenter present,
        IAttachmentStorage storage, IOptions<EscalatedOptions> options)
    {
        _db = db;
        _tickets = tickets;
        _present = present;
        _storage = storage;
        _options = options.Value;
    }

    private string Prefix => "/" + _options.RoutePrefix.Trim('/');

    private string ShowUrl(Ticket ticket) => $"{Prefix}/{Uri.EscapeDataString(ticket.Reference)}";

    /// <summary>The signed-in customer's own ticket, or null (404) for anyone else's.</summary>
    private async Task<Ticket?> OwnAsync(string reference, CancellationToken ct)
    {
        var userId = this.CurrentUserId();
        return await _db.Tickets.FirstOrDefaultAsync(t => t.Reference == reference && t.RequesterId == userId, ct);
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var query = Request.Query;
        string? Get(string key) => string.IsNullOrWhiteSpace(query[key]) ? null : query[key].ToString();
        var keys = new[] { "status", "priority", "ticket_type", "search", "tag", "has_attachments", "created_after", "created_before", "sort_by", "sort_dir" };

        var filters = new TicketListFilters
        {
            RequesterId = this.CurrentUserId(),
            Status = Get("status") is { } status && Statuses.Contains(status) ? TicketStatusExtensions.Parse(status) : null,
            Priority = Get("priority") is { } priority && Priorities.Contains(priority) ? TicketPriorityExtensions.Parse(priority) : null,
            TicketType = Get("ticket_type"),
            Search = Get("search"),
            TagIds = int.TryParse(Get("tag"), out var tag) ? new[] { tag } : null,
            SortBy = Get("sort_by"),
            SortDir = Get("sort_dir"),
        };

        var page = Math.Max(1, int.TryParse(Get("page"), out var p) ? p : 1);
        var (items, total) = await _tickets.ListAsync(filters, page, 15, ct);

        var rows = new List<object?>();
        foreach (var ticket in items)
        {
            rows.Add(await _present.CustomerTicketAsync(ticket, withThread: false, ct));
        }

        var echo = keys.Where(k => Get(k) is not null).ToDictionary(k => k, k => Get(k));

        return UiResults.Render("Escalated/Customer/Index", new
        {
            tickets = UiPresenter.Paginator(rows, total, page, 15, Request.Path, echo),
            filters = echo,
        });
    }

    [HttpGet("create")]
    public async Task<IActionResult> Create(CancellationToken ct) =>
        UiResults.Render("Escalated/Customer/Create", new
        {
            departments = await _present.DepartmentOptionsAsync(ct),
            priorities = Priorities,
        });

    [HttpPost("")]
    public async Task<IActionResult> Store(CancellationToken ct)
    {
        var input = await UiInput.ReadAsync(Request, ct);
        var subject = input.String("subject");
        var description = input.String("description");
        var priority = input.String("priority");
        var departmentId = input.Int("department_id");
        var ticketType = input.String("ticket_type");
        var errors = new Dictionary<string, string>();

        if (subject is null) errors["subject"] = "The subject field is required.";
        else if (subject.Length > 255) errors["subject"] = "The subject may not be greater than 255 characters.";
        if (description is null) errors["description"] = "The description field is required.";
        if (priority is not null && !Priorities.Contains(priority)) errors["priority"] = "The selected priority is invalid.";
        if (input.String("department_id") is not null
            && (departmentId is null || !await _db.Departments.AnyAsync(d => d.Id == departmentId && d.IsActive, ct)))
        {
            errors["department_id"] = "The selected department is invalid.";
        }
        if (ticketType is not null && !Ticket.ValidTypes.Contains(ticketType)) errors["ticket_type"] = "The selected ticket type is invalid.";
        if (UiAttachments.Validate(input.Files, _options.Attachments) is { } attachmentError) errors["attachments"] = attachmentError;
        if (errors.Count > 0) return UiResults.Back(HttpContext, $"{Prefix}/create", errors: errors);

        var ticket = await _tickets.CreateAsync(subject!, description,
            requesterId: this.CurrentUserId(),
            priority: priority is null ? null : TicketPriorityExtensions.Parse(priority),
            departmentId: departmentId,
            ticketType: ticketType,
            ct: ct);
        await UiAttachments.StoreAsync(_db, _storage, input.Files, "ticket", ticket.Id, ct);

        return new UiRedirectResult(ShowUrl(ticket)) { Success = "Ticket created." };
    }

    [HttpGet(Reference)]
    public async Task<IActionResult> Show(string reference, CancellationToken ct)
    {
        var userId = this.CurrentUserId();
        var ticket = await _db.Tickets
            .Include(t => t.Replies.Where(r => !r.IsInternalNote).OrderByDescending(r => r.CreatedAt))
            .Include(t => t.Department)
            .Include(t => t.SatisfactionRating)
            .Include(t => t.ChatSessions)
            .AsSplitQuery()
            .FirstOrDefaultAsync(t => t.Reference == reference && t.RequesterId == userId, ct);
        if (ticket is null) return NotFound();

        await _db.LoadAttachmentsAsync(ticket, ct);
        ticket.PopulateAttachmentUrls(Request);

        return UiResults.Render("Escalated/Customer/Show", new
        {
            ticket = await _present.CustomerTicketAsync(ticket, withThread: true, ct),
        });
    }

    [HttpPost(Reference + "/reply")]
    public async Task<IActionResult> Reply(string reference, CancellationToken ct)
    {
        var ticket = await OwnAsync(reference, ct);
        if (ticket is null) return NotFound();

        var input = await UiInput.ReadAsync(Request, ct);
        var body = input.String("body");
        var errors = new Dictionary<string, string>();
        if (body is null) errors["body"] = "The body field is required.";
        if (UiAttachments.Validate(input.Files, _options.Attachments) is { } attachmentError) errors["attachments"] = attachmentError;
        if (errors.Count > 0) return UiResults.Back(HttpContext, ShowUrl(ticket), errors: errors);

        // A customer never writes internal notes, whatever the composer sends.
        var reply = await _tickets.AddReplyAsync(ticket, body!, this.CurrentUserId(), null, isNote: false, ct);
        await UiAttachments.StoreAsync(_db, _storage, input.Files, "reply", reply.Id, ct);

        return UiResults.Back(HttpContext, ShowUrl(ticket), success: "Reply sent.");
    }

    [HttpPost(Reference + "/close")]
    public async Task<IActionResult> Close(string reference, CancellationToken ct)
    {
        var ticket = await OwnAsync(reference, ct);
        if (ticket is null) return NotFound();
        if (!_options.AllowCustomerClose) return StatusCode(StatusCodes.Status403Forbidden);

        return await ChangeStatusAsync(ticket, TicketStatus.Closed, "Ticket closed.", ct);
    }

    [HttpPost(Reference + "/reopen")]
    public async Task<IActionResult> Reopen(string reference, CancellationToken ct)
    {
        var ticket = await OwnAsync(reference, ct);
        if (ticket is null) return NotFound();

        return await ChangeStatusAsync(ticket, TicketStatus.Reopened, "Ticket reopened.", ct);
    }

    private async Task<IActionResult> ChangeStatusAsync(Ticket ticket, TicketStatus status, string success, CancellationToken ct)
    {
        try
        {
            await _tickets.ChangeStatusAsync(ticket, status, this.CurrentUserId(), ct);
        }
        catch (InvalidOperationException ex)
        {
            return UiResults.Back(HttpContext, ShowUrl(ticket), error: ex.Message);
        }

        return UiResults.Back(HttpContext, ShowUrl(ticket), success: success);
    }

    /// <summary>A 1-5 rating with an optional comment, once, on a resolved or closed ticket.</summary>
    [HttpPost(Reference + "/rate")]
    public async Task<IActionResult> Rate(string reference, CancellationToken ct)
    {
        var ticket = await OwnAsync(reference, ct);
        if (ticket is null) return NotFound();

        var input = await UiInput.ReadAsync(Request, ct);
        var rating = input.Int("rating");
        var comment = input.String("comment");
        var errors = new Dictionary<string, string>();
        if (rating is not (>= 1 and <= 5)) errors["rating"] = "The rating must be between 1 and 5.";
        if (comment is { Length: > 2000 }) errors["comment"] = "The comment may not be greater than 2000 characters.";
        if (errors.Count > 0) return UiResults.Back(HttpContext, ShowUrl(ticket), errors: errors);

        if (ticket.Status is not (TicketStatus.Resolved or TicketStatus.Closed))
        {
            return UiResults.Back(HttpContext, ShowUrl(ticket), error: "Only resolved or closed tickets can be rated.");
        }

        if (await _db.SatisfactionRatings.AnyAsync(r => r.TicketId == ticket.Id, ct))
        {
            return UiResults.Back(HttpContext, ShowUrl(ticket), error: "This ticket has already been rated.");
        }

        _db.SatisfactionRatings.Add(new SatisfactionRating
        {
            TicketId = ticket.Id,
            Rating = rating!.Value,
            Comment = comment,
            RaterType = "user",
            CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync(ct);

        return UiResults.Back(HttpContext, ShowUrl(ticket), success: "Thank you for your feedback.");
    }
}
