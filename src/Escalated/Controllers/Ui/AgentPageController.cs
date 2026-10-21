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

    public AgentPageController(TicketPageServices services, ITicketActionRegistry actions, EscalatedEventDispatcher events)
        : base(services)
    {
        _actions = actions;
        _events = events;
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

    protected override Task AddShowPropsAsync(Ticket ticket, Dictionary<string, object?> props, CancellationToken ct)
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
        return Task.CompletedTask;
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
