using Escalated.Authorization;
using Escalated.Models;
using Escalated.Ui;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Escalated.Controllers.Ui;

/// <summary>
/// The admin ticket pages (<c>Escalated/Admin/Tickets/Index</c> and <c>Show</c>)
/// and their form posts, with escalated-laravel's <c>Admin/TicketController</c>
/// routes and props: the agent screens plus <c>agents</c>, <c>savedViews</c>,
/// <c>is_snoozed</c> and snoozing. <c>GET support/admin/tickets</c> is also the
/// JSON API's ticket list, so it answers only page requests.
/// </summary>
[Route("support/admin")]
[Authorize(Policy = EscalatedPolicies.Admin)]
[EscalatedPage]
public class AdminTicketPageController : TicketPageControllerBase
{
    public AdminTicketPageController(TicketPageServices services)
        : base(services)
    {
    }

    protected override string Area => "admin";

    protected override string IndexComponent => "Escalated/Admin/Tickets/Index";

    protected override string ShowComponent => "Escalated/Admin/Tickets/Show";

    protected override async Task AddIndexPropsAsync(Dictionary<string, object?> props, CancellationToken ct)
    {
        var userId = this.CurrentUserId();
        var views = await Db.SavedViews.AsNoTracking()
            .Where(v => v.UserId == userId || v.IsShared)
            .OrderBy(v => v.Position).ThenBy(v => v.Id)
            .ToListAsync(ct);

        props["agents"] = await Present.AgentOptionsAsync(ct);
        props["savedViews"] = views.Select(SavedView).ToList();
    }

    protected override async Task AddShowPropsAsync(Ticket ticket, Dictionary<string, object?> props, CancellationToken ct)
    {
        props["agents"] = await Present.AgentOptionsAsync(ct);
        props["is_snoozed"] = ticket.IsSnoozed;
    }

    /// <summary>A saved view as the views sidebar reads it, with <c>filters</c> as an object.</summary>
    internal static Dictionary<string, object?> SavedView(SavedView view) => new()
    {
        ["id"] = view.Id,
        ["name"] = view.Name,
        ["user_id"] = view.UserId,
        ["filters"] = UiPresenter.ParseJson(view.Filters) ?? new Dictionary<string, object?>(),
        ["sort_by"] = view.SortBy,
        ["sort_dir"] = view.SortDir,
        ["is_shared"] = view.IsShared,
        ["is_default"] = view.IsDefault,
        ["position"] = view.Position,
        ["created_at"] = view.CreatedAt,
        ["updated_at"] = view.UpdatedAt,
    };
}
