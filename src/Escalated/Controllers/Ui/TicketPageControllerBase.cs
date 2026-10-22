using Escalated.Authorization;
using Escalated.Configuration;
using Escalated.Data;
using Escalated.Enums;
using Escalated.Events;
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
/// The ticket pages the agent and admin areas share (escalated-laravel's
/// <c>Agent/TicketController</c> and <c>Admin/TicketController</c> serve the same
/// screens under two route groups): the queue, one ticket, and the ticket's form
/// posts. Tickets are addressed by reference, as the pages link them. A concrete
/// controller names the area's route, pages and extra props.
/// </summary>
public abstract class TicketPageControllerBase : ControllerBase
{
    /// <summary>A ticket reference (<c>ESC-00042</c>): never a bare number, so it never shadows the API's <c>{id:int}</c> routes.</summary>
    internal const string Reference = "{reference:regex(^\\D.*-\\d+$)}";

    private static readonly HashSet<string> Statuses = Enum.GetValues<TicketStatus>().Select(s => s.ToValue()).ToHashSet();
    private static readonly HashSet<string> Priorities = Enum.GetValues<TicketPriority>().Select(p => p.ToValue()).ToHashSet();

    private readonly AssignmentService _assignment;
    private readonly MacroService _macros;
    private readonly TicketSubjectService _subjects;
    private readonly IAttachmentStorage _storage;
    private readonly UiPresence _presence;

    protected TicketPageControllerBase(TicketPageServices services)
    {
        Db = services.Db;
        Tickets = services.Tickets;
        Present = services.Present;
        Options = services.Options.Value;
        _assignment = services.Assignment;
        _macros = services.Macros;
        _subjects = services.Subjects;
        _storage = services.Storage;
        _presence = services.Presence;
    }

    protected EscalatedDbContext Db { get; }

    protected TicketService Tickets { get; }

    protected UiPresenter Present { get; }

    protected EscalatedOptions Options { get; }

    /// <summary>The route segment and route-name area: <c>agent</c> or <c>admin</c>.</summary>
    protected abstract string Area { get; }

    /// <summary>The queue page's component name.</summary>
    protected abstract string IndexComponent { get; }

    /// <summary>The ticket page's component name.</summary>
    protected abstract string ShowComponent { get; }

    /// <summary>Adds the area's own props to the queue page.</summary>
    protected virtual Task AddIndexPropsAsync(Dictionary<string, object?> props, CancellationToken ct) => Task.CompletedTask;

    /// <summary>Adds the area's own props to the ticket page.</summary>
    protected virtual Task AddShowPropsAsync(Ticket ticket, Dictionary<string, object?> props, CancellationToken ct) => Task.CompletedTask;

    protected string Prefix => "/" + Options.RoutePrefix.Trim('/');

    protected string ShowUrl(Ticket ticket) => $"{Prefix}/{Area}/tickets/{Uri.EscapeDataString(ticket.Reference)}";

    protected Task<Ticket?> FindAsync(string reference, CancellationToken ct) =>
        Db.Tickets.FirstOrDefaultAsync(t => t.Reference == reference, ct);

    [HttpGet("tickets")]
    [EscalatedPage(Negotiate = true)]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var query = Request.Query;
        string? Get(string key) => string.IsNullOrWhiteSpace(query[key]) ? null : query[key].ToString();

        var filters = new TicketListFilters
        {
            Status = Get("status") is { } status && Statuses.Contains(status) ? TicketStatusExtensions.Parse(status) : null,
            Priority = Get("priority") is { } priority && Priorities.Contains(priority) ? TicketPriorityExtensions.Parse(priority) : null,
            TicketType = Get("ticket_type"),
            AssignedTo = Get("assigned_to"),
            Unassigned = Get("unassigned") is "1" or "true" ? true : null,
            DepartmentId = int.TryParse(Get("department_id"), out var departmentId) ? departmentId : null,
            Search = Get("search"),
            SlaBreach = Get("sla_breached") is "1" or "true" ? true : null,
            SortBy = Get("sort_by"),
            SortDir = Get("sort_dir"),
            TagIds = TagIds(query),
            FollowedBy = Get("following") is "1" or "true" ? this.CurrentUserId() : null,
        };

        var page = Math.Max(1, int.TryParse(Get("page"), out var p) ? p : 1);
        var perPage = Math.Clamp(int.TryParse(Get("per_page"), out var pp) ? pp : 25, 1, 100);
        var (items, total) = await Tickets.ListAsync(filters, page, perPage, ct);

        var rows = (await Present.TicketRowsAsync(items, ct)).Cast<object?>().ToList();
        var echo = query.ToDictionary(q => q.Key, q => (string?)q.Value.ToString());

        var props = new Dictionary<string, object?>
        {
            ["tickets"] = UiPresenter.Paginator(rows, total, page, perPage, Request.Path, echo),
            ["filters"] = echo,
            ["departments"] = await Present.DepartmentOptionsAsync(ct),
            ["tags"] = await Present.TagOptionsAsync(ct),
        };
        await AddIndexPropsAsync(props, ct);
        return UiResults.Render(IndexComponent, props);
    }

    private static int[]? TagIds(IQueryCollection query)
    {
        var raw = query["tag_ids"].Concat(query["tag_ids[]"]).Concat(query["tag"])
            .SelectMany(v => (v ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries));
        var ids = raw.Select(v => int.TryParse(v, out var id) ? id : (int?)null).Where(id => id is not null).Select(id => id!.Value).ToArray();
        return ids.Length > 0 ? ids : null;
    }

    [HttpPost("tickets/bulk")]
    [EscalatedPage(Negotiate = true)]
    public async Task<IActionResult> Bulk(CancellationToken ct)
    {
        var input = await UiInput.ReadAsync(Request, ct);
        var ids = input.IntList("ticket_ids");
        var action = input.String("action");
        var fallback = $"{Prefix}/{Area}/tickets";

        if (ids is null || ids.Count == 0 || action is not ("status" or "priority" or "assign" or "tags" or "department" or "close" or "resolve"))
        {
            return UiResults.Back(HttpContext, fallback, errors: new Dictionary<string, string> { ["action"] = "Choose tickets and an action." });
        }

        var value = input.String("value");
        var causer = this.CurrentUserId();
        var done = 0;

        foreach (var id in ids)
        {
            var ticket = await Db.Tickets.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (ticket is null) continue;

            try
            {
                switch (action)
                {
                    case "status" when value is not null && Statuses.Contains(value):
                        await Tickets.ChangeStatusAsync(ticket, TicketStatusExtensions.Parse(value), causer, ct);
                        break;
                    case "close":
                        await Tickets.ChangeStatusAsync(ticket, TicketStatus.Closed, causer, ct);
                        break;
                    case "resolve":
                        await Tickets.ChangeStatusAsync(ticket, TicketStatus.Resolved, causer, ct);
                        break;
                    case "priority" when value is not null && Priorities.Contains(value):
                        await Tickets.ChangePriorityAsync(ticket, TicketPriorityExtensions.Parse(value), causer, ct);
                        break;
                    case "assign" when value is not null:
                        await _assignment.AssignAsync(ticket, value, causer, ct);
                        break;
                    case "department" when int.TryParse(value, out var departmentId) && await Db.Departments.AnyAsync(d => d.Id == departmentId, ct):
                        await Tickets.ChangeDepartmentAsync(ticket, departmentId, causer, ct);
                        break;
                    case "tags" when input.IntList("value") is { Count: > 0 } tagIds:
                        await Tickets.AddTagsAsync(ticket, tagIds.ToArray(), causer, ct);
                        break;
                    default:
                        continue;
                }

                done++;
            }
            catch (InvalidOperationException)
            {
                // A status this ticket cannot move to; the others still change.
            }
        }

        return UiResults.Back(HttpContext, fallback, success: $"{done} ticket(s) updated.");
    }

    [HttpGet("tickets/" + Reference)]
    public async Task<IActionResult> Show(string reference, CancellationToken ct)
    {
        var ticket = await Db.Tickets
            .Include(t => t.Replies.OrderByDescending(r => r.CreatedAt))
            .Include(t => t.Activities.OrderByDescending(a => a.CreatedAt).Take(20))
            .Include(t => t.SatisfactionRating)
            .Include(t => t.ChatSessions)
            .Include(t => t.SlaPolicy)
            .Include(t => t.LinksAsParent).ThenInclude(l => l.ChildTicket)
            .Include(t => t.LinksAsChild).ThenInclude(l => l.ParentTicket)
            .Include(t => t.Subjects.OrderBy(s => s.Position))
            .AsSplitQuery()
            .FirstOrDefaultAsync(t => t.Reference == reference, ct);
        if (ticket is null) return NotFound();

        var userId = this.CurrentUserId()!;
        await Db.LoadAttachmentsAsync(ticket, ct);
        ticket.PopulateAttachmentUrls(Request);
        ticket.PopulateComputedFields();
        await _subjects.PopulateTicketSubjectsAsync(ticket, ct);

        var props = await TicketPropsAsync(ticket, ct);

        var canned = await Db.CannedResponses.AsNoTracking()
            .Where(c => c.IsShared || c.CreatedBy == userId).OrderBy(c => c.Title).ToListAsync(ct);
        var macros = await Db.Macros.AsNoTracking()
            .Where(m => m.IsShared || m.CreatedBy == userId).OrderBy(m => m.Order).ThenBy(m => m.Id).ToListAsync(ct);

        var page = new Dictionary<string, object?>
        {
            ["ticket"] = props,
            ["departments"] = await Present.DepartmentOptionsAsync(ct),
            ["tags"] = await Present.TagOptionsAsync(ct),
            ["cannedResponses"] = canned.Select(c => new Dictionary<string, object?>
            {
                ["id"] = c.Id,
                ["title"] = c.Title,
                ["body"] = c.Body,
                ["category"] = c.Category,
                ["is_shared"] = c.IsShared,
            }).ToList(),
            ["macros"] = macros.Select(m => new Dictionary<string, object?>
            {
                ["id"] = m.Id,
                ["name"] = m.Name,
                ["description"] = m.Description,
                ["actions"] = UiPresenter.ParseJson(m.Actions),
                ["is_shared"] = m.IsShared,
                ["order"] = m.Order,
            }).ToList(),
            ["is_following"] = await Db.TicketFollowers.AnyAsync(f => f.TicketId == ticket.Id && f.UserId == userId, ct),
            ["followers_count"] = await Db.TicketFollowers.CountAsync(f => f.TicketId == ticket.Id, ct),
            ["custom_fields"] = await new CustomFieldService(Db).TicketValuesAsync(ticket.Id, ct),
        };
        await AddShowPropsAsync(ticket, page, ct);
        return UiResults.Render(ShowComponent, page);
    }

    private async Task<Dictionary<string, object?>> TicketPropsAsync(Ticket ticket, CancellationToken ct)
    {
        var props = await Present.TicketBaseAsync(ticket, ct);

        props["department"] = ticket.DepartmentId is { } departmentId
            ? await Db.Departments.AsNoTracking().Where(d => d.Id == departmentId).Select(d => UiPresenter.Department(d)).FirstOrDefaultAsync(ct)
            : null;
        props["tags"] = (await Db.TicketTags.AsNoTracking().Where(tt => tt.TicketId == ticket.Id)
                .Join(Db.Tags.AsNoTracking(), tt => tt.TagId, tag => tag.Id, (tt, tag) => tag).ToListAsync(ct))
            .Select(UiPresenter.Tag).ToList();
        props["sla_policy"] = ticket.SlaPolicy is { } sla ? new Dictionary<string, object?> { ["id"] = sla.Id, ["name"] = sla.Name } : null;

        var replies = new List<Dictionary<string, object?>>();
        foreach (var reply in ticket.Replies.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id))
        {
            replies.Add(await Present.ReplyAsync(reply, ticket, ct));
        }
        props["replies"] = replies;
        props["pinned_notes"] = replies.Where(r => r["is_internal_note"] is true && r["is_pinned"] is true).ToList();
        props["attachments"] = ticket.Attachments.Select(UiPresenter.Attachment).ToList();

        var activities = new List<Dictionary<string, object?>>();
        foreach (var activity in ticket.Activities.OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id))
        {
            activities.Add(await Present.ActivityAsync(activity, ct));
        }
        props["activities"] = activities;

        props["satisfaction_rating"] = UiPresenter.SatisfactionRating(ticket.SatisfactionRating);
        props["chat_session_id"] = ticket.ChatSessionId;
        props["chat_started_at"] = ticket.ChatStartedAt;
        props["chat_metadata"] = UiPresenter.ParseJson(ticket.ChatMetadata);
        props["chat_messages"] = ticket.ChatSessionId is null
            ? new List<object?>()
            : replies.AsEnumerable().Reverse().Where(r => r["is_internal_note"] is false).Cast<object?>().ToList();
        props["is_live_chat"] = ticket.IsLiveChat;
        props["related_tickets"] = ticket.RelatedTickets ?? Array.Empty<object>();
        props["subjects"] = ticket.SubjectsPayload ?? Array.Empty<Dtos.TicketSubjectResponse>();
        props["requester_ticket_count"] = ticket.RequesterId is not null
            ? await Db.Tickets.CountAsync(t => t.RequesterType == ticket.RequesterType && t.RequesterId == ticket.RequesterId, ct)
            : ticket.GuestEmail is not null ? await Db.Tickets.CountAsync(t => t.GuestEmail == ticket.GuestEmail, ct) : 0;

        return props;
    }

    /// <summary>
    /// <c>PUT tickets/{ticket}</c> (escalated-laravel's <c>tickets.update</c>): saves the
    /// <c>custom_fields</c> sent, checking only those keys. Omitting it changes nothing.
    /// </summary>
    [HttpPut("tickets/" + Reference)]
    [HttpPatch("tickets/" + Reference)]
    public async Task<IActionResult> Update(string reference, CancellationToken ct)
    {
        var ticket = await FindAsync(reference, ct);
        if (ticket is null) return NotFound();

        var input = await UiInput.ReadAsync(Request, ct);
        var customFields = new CustomFieldService(Db);
        var plan = await customFields.ValidateTicketAsync(input.Object("custom_fields"), creating: false, ct);
        if (!plan.IsValid) return UiResults.Back(HttpContext, ShowUrl(ticket), errors: plan.Errors);

        await customFields.ApplyTicketAsync(ticket.Id, plan, ct);
        return UiResults.Back(HttpContext, ShowUrl(ticket), success: "Ticket updated.");
    }

    [HttpPost("tickets/" + Reference + "/reply")]
    public Task<IActionResult> Reply(string reference, CancellationToken ct) => ReplyOrNoteAsync(reference, note: false, ct);

    [HttpPost("tickets/" + Reference + "/note")]
    public Task<IActionResult> Note(string reference, CancellationToken ct) => ReplyOrNoteAsync(reference, note: true, ct);

    private async Task<IActionResult> ReplyOrNoteAsync(string reference, bool note, CancellationToken ct)
    {
        var ticket = await FindAsync(reference, ct);
        if (ticket is null) return NotFound();

        var input = await UiInput.ReadAsync(Request, ct);
        var body = input.String("body");
        var errors = new Dictionary<string, string>();
        if (body is null) errors["body"] = "The body field is required.";
        if (UiAttachments.Validate(input.Files, Options.Attachments) is { } attachmentError) errors["attachments"] = attachmentError;
        if (errors.Count > 0) return UiResults.Back(HttpContext, ShowUrl(ticket), errors: errors);

        // The reply composer posts both kinds to the reply route, flagging notes.
        var isNote = note || input.Bool("is_internal_note");
        var reply = await Tickets.AddReplyAsync(ticket, body!, this.CurrentUserId(), null, isNote, ct);
        await UiAttachments.StoreAsync(Db, _storage, input.Files, "reply", reply.Id, ct);

        return UiResults.Back(HttpContext, ShowUrl(ticket), success: isNote ? "Note added." : "Reply sent.");
    }

    [HttpPost("tickets/" + Reference + "/assign")]
    public async Task<IActionResult> Assign(string reference, CancellationToken ct)
    {
        var ticket = await FindAsync(reference, ct);
        if (ticket is null) return NotFound();

        var input = await UiInput.ReadAsync(Request, ct);
        if (input.String("agent_id") is not { } agentId)
        {
            return UiResults.Back(HttpContext, ShowUrl(ticket), errors: new Dictionary<string, string> { ["agent_id"] = "The agent id field is required." });
        }

        await _assignment.AssignAsync(ticket, agentId, this.CurrentUserId(), ct);
        return UiResults.Back(HttpContext, ShowUrl(ticket), success: "Ticket assigned.");
    }

    [HttpPost("tickets/" + Reference + "/status")]
    public async Task<IActionResult> Status(string reference, CancellationToken ct)
    {
        var ticket = await FindAsync(reference, ct);
        if (ticket is null) return NotFound();

        var input = await UiInput.ReadAsync(Request, ct);
        if (input.String("status") is not { } status || !Statuses.Contains(status))
        {
            return UiResults.Back(HttpContext, ShowUrl(ticket), errors: new Dictionary<string, string> { ["status"] = "The selected status is invalid." });
        }

        try
        {
            await Tickets.ChangeStatusAsync(ticket, TicketStatusExtensions.Parse(status), this.CurrentUserId(), ct);
        }
        catch (InvalidOperationException ex)
        {
            return UiResults.Back(HttpContext, ShowUrl(ticket), errors: new Dictionary<string, string> { ["status"] = ex.Message });
        }

        return UiResults.Back(HttpContext, ShowUrl(ticket), success: "Status updated.");
    }

    [HttpPost("tickets/" + Reference + "/priority")]
    public async Task<IActionResult> Priority(string reference, CancellationToken ct)
    {
        var ticket = await FindAsync(reference, ct);
        if (ticket is null) return NotFound();

        var input = await UiInput.ReadAsync(Request, ct);
        if (input.String("priority") is not { } priority || !Priorities.Contains(priority))
        {
            return UiResults.Back(HttpContext, ShowUrl(ticket), errors: new Dictionary<string, string> { ["priority"] = "The selected priority is invalid." });
        }

        await Tickets.ChangePriorityAsync(ticket, TicketPriorityExtensions.Parse(priority), this.CurrentUserId(), ct);
        return UiResults.Back(HttpContext, ShowUrl(ticket), success: "Priority updated.");
    }

    /// <summary>Sets the ticket's tags to <c>tag_ids</c>: adds the new ones and removes the rest.</summary>
    [HttpPost("tickets/" + Reference + "/tags")]
    public async Task<IActionResult> Tags(string reference, CancellationToken ct)
    {
        var ticket = await FindAsync(reference, ct);
        if (ticket is null) return NotFound();

        var input = await UiInput.ReadAsync(Request, ct);
        var wanted = input.IntList("tag_ids");
        if (wanted is null)
        {
            return UiResults.Back(HttpContext, ShowUrl(ticket), errors: new Dictionary<string, string> { ["tag_ids"] = "The tag ids must be integers." });
        }

        var known = await Db.Tags.Where(t => wanted.Contains(t.Id)).Select(t => t.Id).ToListAsync(ct);
        if (known.Count != wanted.Distinct().Count())
        {
            return UiResults.Back(HttpContext, ShowUrl(ticket), errors: new Dictionary<string, string> { ["tag_ids"] = "The selected tags are invalid." });
        }

        var current = await Db.TicketTags.Where(tt => tt.TicketId == ticket.Id).Select(tt => tt.TagId).ToListAsync(ct);
        var add = known.Except(current).ToArray();
        var remove = current.Except(known).ToArray();
        var causer = this.CurrentUserId();
        if (add.Length > 0) await Tickets.AddTagsAsync(ticket, add, causer, ct);
        if (remove.Length > 0) await Tickets.RemoveTagsAsync(ticket, remove, causer, ct);

        return UiResults.Back(HttpContext, ShowUrl(ticket), success: "Tags updated.");
    }

    [HttpPost("tickets/" + Reference + "/department")]
    public async Task<IActionResult> Department(string reference, CancellationToken ct)
    {
        var ticket = await FindAsync(reference, ct);
        if (ticket is null) return NotFound();

        var input = await UiInput.ReadAsync(Request, ct);
        if (input.Int("department_id") is not { } departmentId || !await Db.Departments.AnyAsync(d => d.Id == departmentId, ct))
        {
            return UiResults.Back(HttpContext, ShowUrl(ticket), errors: new Dictionary<string, string> { ["department_id"] = "The selected department is invalid." });
        }

        await Tickets.ChangeDepartmentAsync(ticket, departmentId, this.CurrentUserId(), ct);
        return UiResults.Back(HttpContext, ShowUrl(ticket), success: "Department updated.");
    }

    [HttpPost("tickets/" + Reference + "/macro")]
    public async Task<IActionResult> Macro(string reference, CancellationToken ct)
    {
        var ticket = await FindAsync(reference, ct);
        if (ticket is null) return NotFound();

        var userId = this.CurrentUserId()!;
        var input = await UiInput.ReadAsync(Request, ct);
        var macroId = input.Int("macro_id");
        var macro = macroId is null ? null
            : await Db.Macros.FirstOrDefaultAsync(m => m.Id == macroId && (m.IsShared || m.CreatedBy == userId), ct);
        if (macro is null)
        {
            return UiResults.Back(HttpContext, ShowUrl(ticket), errors: new Dictionary<string, string> { ["macro_id"] = "The selected macro is invalid." });
        }

        await _macros.ApplyAsync(macro, ticket, userId, ct);
        return UiResults.Back(HttpContext, ShowUrl(ticket), success: $"Macro \"{macro.Name}\" applied.");
    }

    /// <summary>Follows the ticket, or stops following it when already following.</summary>
    [HttpPost("tickets/" + Reference + "/follow")]
    public async Task<IActionResult> Follow(string reference, CancellationToken ct)
    {
        var ticket = await FindAsync(reference, ct);
        if (ticket is null) return NotFound();

        var userId = this.CurrentUserId()!;
        var existing = await Db.TicketFollowers.FirstOrDefaultAsync(f => f.TicketId == ticket.Id && f.UserId == userId, ct);
        if (existing is not null)
        {
            Db.TicketFollowers.Remove(existing);
            await Db.SaveChangesAsync(ct);
            return UiResults.Back(HttpContext, ShowUrl(ticket), success: "You are no longer following this ticket.");
        }

        Db.TicketFollowers.Add(new TicketFollower { TicketId = ticket.Id, UserId = userId, CreatedAt = DateTime.UtcNow });
        await Db.SaveChangesAsync(ct);
        return UiResults.Back(HttpContext, ShowUrl(ticket), success: "You are now following this ticket.");
    }

    /// <summary>Pins an internal note, or unpins it when pinned. Public replies cannot be pinned.</summary>
    [HttpPost("tickets/" + Reference + "/replies/{reply:int}/pin")]
    public async Task<IActionResult> Pin(string reference, int reply, CancellationToken ct)
    {
        var ticket = await FindAsync(reference, ct);
        if (ticket is null) return NotFound();

        var note = await Db.Replies.FirstOrDefaultAsync(r => r.Id == reply && r.TicketId == ticket.Id, ct);
        if (note is null) return NotFound();
        if (!note.IsInternalNote)
        {
            return UiResults.Back(HttpContext, ShowUrl(ticket), error: "Only internal notes can be pinned.");
        }

        note.IsPinned = !note.IsPinned;
        note.UpdatedAt = DateTime.UtcNow;
        await Db.SaveChangesAsync(ct);
        return UiResults.Back(HttpContext, ShowUrl(ticket), success: note.IsPinned ? "Note pinned." : "Note unpinned.");
    }

    /// <summary>
    /// Marks the signed-in user as viewing the ticket and answers the other staff
    /// viewing it, as <c>{viewers: [{id, name}]}</c>. The page polls it.
    /// </summary>
    [HttpPost("tickets/" + Reference + "/presence")]
    public async Task<IActionResult> Presence(string reference, CancellationToken ct)
    {
        var ticket = await FindAsync(reference, ct);
        if (ticket is null) return NotFound();

        var userId = this.CurrentUserId()!;
        var user = await Present.UserAsync(userId, ct);
        var viewers = _presence.Touch(ticket.Id, userId, user?["name"] as string ?? userId);
        return new JsonResult(new
        {
            viewers = viewers.Select(v => new Dictionary<string, object?> { ["id"] = v.Id, ["name"] = v.Name }).ToList(),
        });
    }
}
