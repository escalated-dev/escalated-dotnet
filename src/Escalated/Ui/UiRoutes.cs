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
