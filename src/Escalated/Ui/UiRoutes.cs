namespace Escalated.Ui;

/// <summary>
/// The named routes the shared pages pass to <c>route(name, params)</c>, with
/// escalated-laravel's names and paths. The default root view hands them to the
/// bundled <c>window.route</c> helper, which fills <c>{placeholders}</c> in order
/// from the params and adds the rest as a query string.
///
/// <para>Paths start with <c>{prefix}</c>, replaced by <see cref="Configuration.EscalatedOptions.RoutePrefix"/>.
/// Hosts can add or replace names (<c>services.Configure&lt;UiRouteOptions&gt;</c>).
/// A page asking for a name not listed gets <c>#</c> and a console warning, not
/// an exception that would blank the screen.</para>
/// </summary>
public sealed class UiRouteOptions
{
    public Dictionary<string, string> Routes { get; } = new(UiRoutes.Defaults);
}

public static class UiRoutes
{
    public static IReadOnlyDictionary<string, string> Defaults { get; } = new Dictionary<string, string>
    {
        // Customer
        ["escalated.customer.tickets.index"] = "{prefix}",
        ["escalated.customer.tickets.create"] = "{prefix}/create",
        ["escalated.customer.tickets.store"] = "{prefix}",
        ["escalated.customer.tickets.show"] = "{prefix}/{ticket}",
        ["escalated.customer.tickets.reply"] = "{prefix}/{ticket}/reply",
        ["escalated.customer.tickets.close"] = "{prefix}/{ticket}/close",
        ["escalated.customer.tickets.reopen"] = "{prefix}/{ticket}/reopen",
        ["escalated.customer.tickets.rate"] = "{prefix}/{ticket}/rate",

        // Agent
        ["escalated.agent.dashboard"] = "{prefix}/agent",
        ["escalated.agent.tickets.index"] = "{prefix}/agent/tickets",
        ["escalated.agent.tickets.bulk"] = "{prefix}/agent/tickets/bulk",
        ["escalated.agent.tickets.show"] = "{prefix}/agent/tickets/{ticket}",
        ["escalated.agent.tickets.reply"] = "{prefix}/agent/tickets/{ticket}/reply",
        ["escalated.agent.tickets.note"] = "{prefix}/agent/tickets/{ticket}/note",
        ["escalated.agent.tickets.assign"] = "{prefix}/agent/tickets/{ticket}/assign",
        ["escalated.agent.tickets.status"] = "{prefix}/agent/tickets/{ticket}/status",
        ["escalated.agent.tickets.priority"] = "{prefix}/agent/tickets/{ticket}/priority",
        ["escalated.agent.tickets.tags"] = "{prefix}/agent/tickets/{ticket}/tags",
        ["escalated.agent.tickets.department"] = "{prefix}/agent/tickets/{ticket}/department",
        ["escalated.agent.tickets.macro"] = "{prefix}/agent/tickets/{ticket}/macro",
        ["escalated.agent.tickets.follow"] = "{prefix}/agent/tickets/{ticket}/follow",
        ["escalated.agent.tickets.custom-action"] = "{prefix}/agent/tickets/{ticket}/actions/{action}",
        ["escalated.agent.tickets.pin"] = "{prefix}/agent/tickets/{ticket}/replies/{reply}/pin",
        ["escalated.agent.tickets.presence"] = "{prefix}/agent/tickets/{ticket}/presence",

        // Admin tickets
        ["escalated.admin.tickets.index"] = "{prefix}/admin/tickets",
        ["escalated.admin.tickets.bulk"] = "{prefix}/admin/tickets/bulk",
        ["escalated.admin.tickets.show"] = "{prefix}/admin/tickets/{ticket}",
        ["escalated.admin.tickets.reply"] = "{prefix}/admin/tickets/{ticket}/reply",
        ["escalated.admin.tickets.note"] = "{prefix}/admin/tickets/{ticket}/note",
        ["escalated.admin.tickets.assign"] = "{prefix}/admin/tickets/{ticket}/assign",
        ["escalated.admin.tickets.status"] = "{prefix}/admin/tickets/{ticket}/status",
        ["escalated.admin.tickets.priority"] = "{prefix}/admin/tickets/{ticket}/priority",
        ["escalated.admin.tickets.tags"] = "{prefix}/admin/tickets/{ticket}/tags",
        ["escalated.admin.tickets.department"] = "{prefix}/admin/tickets/{ticket}/department",
        ["escalated.admin.tickets.macro"] = "{prefix}/admin/tickets/{ticket}/macro",
        ["escalated.admin.tickets.follow"] = "{prefix}/admin/tickets/{ticket}/follow",
        ["escalated.admin.tickets.pin"] = "{prefix}/admin/tickets/{ticket}/replies/{reply}/pin",
        ["escalated.admin.tickets.presence"] = "{prefix}/admin/tickets/{ticket}/presence",
        ["escalated.admin.tickets.snooze"] = "{prefix}/admin/tickets/{ticket}/snooze",
        ["escalated.admin.tickets.unsnooze"] = "{prefix}/admin/tickets/{ticket}/unsnooze",
        ["escalated.admin.saved-views.index"] = "{prefix}/admin/saved-views",
        ["escalated.admin.saved-views.store"] = "{prefix}/admin/saved-views",
        ["escalated.admin.saved-views.update"] = "{prefix}/admin/saved-views/{savedView}",
        ["escalated.admin.saved-views.destroy"] = "{prefix}/admin/saved-views/{savedView}",

        // Admin
        ["escalated.admin.settings"] = "{prefix}/admin/settings",
        ["escalated.admin.settings.update"] = "{prefix}/admin/settings",
        ["escalated.admin.departments.index"] = "{prefix}/admin/departments",
        ["escalated.admin.departments.create"] = "{prefix}/admin/departments/create",
        ["escalated.admin.departments.store"] = "{prefix}/admin/departments",
        ["escalated.admin.departments.edit"] = "{prefix}/admin/departments/{department}/edit",
        ["escalated.admin.departments.update"] = "{prefix}/admin/departments/{department}",
        ["escalated.admin.departments.destroy"] = "{prefix}/admin/departments/{department}",
        ["escalated.admin.tags.index"] = "{prefix}/admin/tags",
        ["escalated.admin.tags.store"] = "{prefix}/admin/tags",
        ["escalated.admin.tags.update"] = "{prefix}/admin/tags/{tag}",
        ["escalated.admin.tags.destroy"] = "{prefix}/admin/tags/{tag}",
        ["escalated.admin.workflows.index"] = "{prefix}/admin/workflows",
        ["escalated.admin.workflows.create"] = "{prefix}/admin/workflows/create",
        ["escalated.admin.workflows.store"] = "{prefix}/admin/workflows",
        ["escalated.admin.workflows.edit"] = "{prefix}/admin/workflows/{workflow}",
        ["escalated.admin.workflows.update"] = "{prefix}/admin/workflows/{workflow}",
        ["escalated.admin.workflows.destroy"] = "{prefix}/admin/workflows/{workflow}",
        ["escalated.admin.workflows.toggle"] = "{prefix}/admin/workflows/{workflow}/toggle",
        ["escalated.admin.workflows.reorder"] = "{prefix}/admin/workflows/reorder",
        ["escalated.admin.workflows.logs"] = "{prefix}/admin/workflows/logs",
    };

    /// <summary>The routes with <c>{prefix}</c> filled in, as absolute paths.</summary>
    public static Dictionary<string, string> Resolve(IReadOnlyDictionary<string, string> routes, string prefix)
    {
        var trimmed = prefix.Trim('/');
        return routes.ToDictionary(
            pair => pair.Key,
            pair => "/" + pair.Value.Replace("{prefix}", trimmed).TrimStart('/'));
    }
}
