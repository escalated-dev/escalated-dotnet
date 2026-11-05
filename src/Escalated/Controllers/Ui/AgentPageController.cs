using Escalated.Authorization;
using Escalated.Configuration;
using Escalated.Enums;
using Escalated.Events;
using Escalated.Models;
using Escalated.Services;
using Escalated.Ui;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Controllers.Ui;

/// <summary>
/// The agent pages (<c>Escalated/Agent/Dashboard</c>, <c>TicketIndex</c>,
/// <c>TicketShow</c>) and their form posts, with escalated-laravel's routes and
/// props (<c>Agent/DashboardController</c>, <c>Agent/TicketController</c>). Tickets
/// are addressed by reference, as the pages link them. Only served when
/// <c>Escalated:Ui:Enabled</c> is on.
/// </summary>
[Route("support/agent")]
[Authorize(Policy = EscalatedPolicies.Agent)]
[EscalatedPage]
public class AgentPageController : TicketPageControllerBase
{
    private readonly ITicketActionRegistry _actions;
    private readonly EscalatedEventDispatcher _events;
    private readonly AgentInbox _inbox;
    private AgentQueue? _queue;

    public AgentPageController(TicketPageServices services, ITicketActionRegistry actions, EscalatedEventDispatcher events, AgentInbox inbox)
        : base(services)
    {
        _actions = actions;
        _events = events;
        _inbox = inbox;
    }

    protected override string Area => "agent";

    protected override string IndexComponent => "Escalated/Agent/TicketIndex";

    protected override string ShowComponent => "Escalated/Agent/TicketShow";

    private static IQueryable<Ticket> Open(IQueryable<Ticket> query) =>
        query.Where(t => t.Status != TicketStatus.Resolved && t.Status != TicketStatus.Closed);

    [HttpGet("")]
    public async Task<IActionResult> Dashboard(CancellationToken ct)
    {
        var userId = this.CurrentUserId()!;
        var today = DateTime.UtcNow.Date;
        var weekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var tickets = Db.Tickets.AsNoTracking();

        var recent = await tickets.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id).Take(10).ToListAsync(ct);
        var breaching = await Open(tickets).Where(t => t.SlaFirstResponseBreached || t.SlaResolutionBreached)
            .OrderBy(t => t.Id).Take(5).ToListAsync(ct);
        var urgent = await Open(tickets).Where(t => t.AssignedTo == null
                && (t.Priority == TicketPriority.Urgent || t.Priority == TicketPriority.Critical))
            .OrderBy(t => t.Id).Take(5).ToListAsync(ct);

        return UiResults.Render("Escalated/Agent/Dashboard", new
        {
            stats = new Dictionary<string, object?>
            {
                ["open"] = await Open(tickets).CountAsync(ct),
                ["my_assigned"] = await Open(tickets).CountAsync(t => t.AssignedTo == userId, ct),
                ["unassigned"] = await Open(tickets).CountAsync(t => t.AssignedTo == null, ct),
                ["sla_breached"] = await Open(tickets).CountAsync(t => t.SlaFirstResponseBreached || t.SlaResolutionBreached, ct),
                ["resolved_today"] = await tickets.CountAsync(t => t.ResolvedAt >= today, ct),
            },
            recentTickets = await Present.TicketRowsAsync(recent, ct),
            needsAttention = new Dictionary<string, object?>
            {
                ["sla_breaching"] = await Present.TicketRowsAsync(breaching, ct),
                ["unassigned_urgent"] = await Present.TicketRowsAsync(urgent, ct),
            },
            myPerformance = new Dictionary<string, object?>
            {
                ["resolved_this_week"] = await tickets.CountAsync(t => t.AssignedTo == userId && t.ResolvedAt >= weekStart, ct),
            },
        });
    }

    /// <summary>
    /// The queue the list shows (<see cref="AgentInbox"/>): <c>assigned=me</c> is
    /// the agent, <c>view=&lt;id&gt;</c> lists by that view's filters, and the queue
    /// is remembered for the ticket page.
    /// </summary>
    protected override async Task<IReadOnlyDictionary<string, string?>> ListQueryAsync(Dictionary<string, string?> query, CancellationToken ct)
    {
        var agentId = this.CurrentUserId();
        var normalised = AgentInbox.Normalise(query, agentId);
        _queue = await _inbox.QueueForAsync(normalised, agentId, ct);
        _inbox.Remember(HttpContext, _queue, Prefix);
        return AgentInbox.ListQuery(normalised, _queue);
    }

    /// <summary>The agent panel's <c>queue</c> and <c>inbox</c> props.</summary>
    protected override async Task AddIndexPropsAsync(Dictionary<string, object?> props, CancellationToken ct)
    {
        var queue = _queue ?? await _inbox.RecalledAsync(HttpContext, this.CurrentUserId(), ct);
        props["queue"] = queue.Prop();
        props["inbox"] = await _inbox.InboxAsync(this.CurrentUserId(), queue, ct);
        await WithPanelFieldsAsync(props, ct);
    }

    /// <summary>is_unread and the requester's presence on every row (ADR 2026-11-05-agent-panel-data), one query each for the page.</summary>
    private async Task WithPanelFieldsAsync(Dictionary<string, object?> props, CancellationToken ct)
    {
        var panel = HttpContext.RequestServices.GetService(typeof(AgentPanelService)) as AgentPanelService;
        if (panel is null || !props.TryGetValue("tickets", out var tickets) || tickets is not IDictionary<string, object?> paginator
            || !paginator.TryGetValue("data", out var data) || data is not IEnumerable<object?> rows)
        {
            return;
        }

        var list = rows.OfType<IDictionary<string, object?>>().ToList();
        var ids = list.Select(row => row.TryGetValue("id", out var id) ? id : null).OfType<int>().ToList();
        var fields = await panel.RowFieldsAsync(this.CurrentUserId(), ids, ct);
        foreach (var row in list)
        {
            if (row.TryGetValue("id", out var value) && value is int id && fields.TryGetValue(id, out var extra))
            {
                foreach (var pair in extra) row[pair.Key] = pair.Value;
            }
        }
    }

    protected override async Task AddShowPropsAsync(Ticket ticket, Dictionary<string, object?> props, CancellationToken ct)
    {
        var userId = this.CurrentUserId();
        props["customActions"] = _actions.ForTicket(ticket, userId).Select(action =>
        {
            var row = new Dictionary<string, object?>(action)
            {
                ["url"] = $"{ShowUrl(ticket)}/actions/{Uri.EscapeDataString(action["key"]?.ToString() ?? string.Empty)}",
                ["method"] = "post",
            };
            return row;
        }).ToList();

        props["agents"] = (await AssignableAgentsAsync(ticket, ct))
            .Select(agent => new Dictionary<string, object?> { ["id"] = agent.GetValueOrDefault("id"), ["name"] = agent.GetValueOrDefault("name") })
            .ToList();
        if (props.GetValueOrDefault("ticket") is Dictionary<string, object?> ticketProps)
        {
            ticketProps["requester_tickets"] = await RequesterTicketsAsync(ticket, ct);
        }

        // Opening the ticket in the agent panel is reading it; the page carries is_unread
        // (false) and the requester's presence.
        if (HttpContext.RequestServices.GetService(typeof(AgentPanelService)) is AgentPanelService panel)
        {
            await panel.MarkReadAsync(userId, ticket.Id, ct);
            if (props.GetValueOrDefault("ticket") is Dictionary<string, object?> detail
                && (await panel.RowFieldsAsync(userId, new[] { ticket.Id }, ct)).TryGetValue(ticket.Id, out var fields))
            {
                foreach (var (key, value) in fields) detail[key] = value;
            }
        }

        var queue = await _inbox.RecalledAsync(HttpContext, userId, ct);
        props["queue"] = queue.Prop();
        props["inbox"] = await _inbox.InboxAsync(userId, queue, ct);
    }

    /// <summary>
    /// The requester's five most recent other tickets the agent may see (the
    /// ticket visibility filter applies), matched as <c>requester_ticket_count</c>
    /// is: the requester, or a guest's email. Newest first; none without a requester.
    /// </summary>
    private async Task<List<Dictionary<string, object?>>> RequesterTicketsAsync(Ticket ticket, CancellationToken ct)
    {
        var others = Db.Tickets.AsNoTracking().Where(t => t.Id != ticket.Id);
        if (ticket.RequesterId is not null)
        {
            others = others.Where(t => t.RequesterType == ticket.RequesterType && t.RequesterId == ticket.RequesterId);
        }
        else if (ticket.GuestEmail is not null)
        {
            others = others.Where(t => t.GuestEmail == ticket.GuestEmail);
        }
        else
        {
            return new List<Dictionary<string, object?>>();
        }

        var rows = await others.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id).Take(5)
            .Select(t => new { t.Id, t.Reference, t.Subject, t.Status, t.CreatedAt, t.UpdatedAt })
            .ToListAsync(ct);

        return rows.Select(t => new Dictionary<string, object?>
        {
            ["id"] = t.Id,
            ["reference"] = t.Reference,
            ["subject"] = t.Subject,
            ["status"] = t.Status.ToValue(),
            ["created_at"] = t.CreatedAt,
            ["updated_at"] = t.UpdatedAt,
        }).ToList();
    }

    /// <summary>
    /// The staff the ticket can be assigned to: agents and admins who can see it
    /// (escalated-laravel's <c>AssignableAgents::for</c>). Every agent sees every
    /// ticket unless visibility is restricted by department or account.
    /// </summary>
    private async Task<List<Dictionary<string, object?>>> AssignableAgentsAsync(Ticket ticket, CancellationToken ct)
    {
        var agents = await Present.AgentOptionsAsync(ct);
        var access = HttpContext.RequestServices.GetService(typeof(TicketAccess)) as TicketAccess;
        if (access is null || !access.IsRestricted)
        {
            return agents;
        }

        var visible = new List<Dictionary<string, object?>>();
        foreach (var agent in agents)
        {
            var id = agent.GetValueOrDefault("id")?.ToString();
            if (id is not null && await access.CanViewAsync(await access.PrincipalAsync(id), ticket.Id, ct))
            {
                visible.Add(agent);
            }
        }

        return visible;
    }

    [HttpPost("tickets/" + Reference + "/actions/{action}")]
    public async Task<IActionResult> CustomAction(string reference, string action, CancellationToken ct)
    {
        var ticket = await FindAsync(reference, ct);
        if (ticket is null) return NotFound();

        var config = _actions.Find(action);
        if (config is null || !config.Visible) return NotFound();
        if (!config.Enabled) return StatusCode(StatusCodes.Status403Forbidden);

        var userId = this.CurrentUserId();
        await Tickets.AddReplyAsync(ticket, $"Custom action \"{action}\" was triggered.", userId, "system", isNote: true, ct);
        await _events.DispatchAsync(new TicketCustomActionTriggeredEvent(ticket, action, userId, null, config.Metadata), ct);

        return UiResults.Back(HttpContext, ShowUrl(ticket), success: "Custom action dispatched.");
    }
}
