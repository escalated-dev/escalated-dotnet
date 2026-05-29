using Escalated.Models;
using Microsoft.Extensions.Options;

namespace Escalated.Configuration;

/// <summary>
/// Resolves host-defined custom ticket actions (registered via
/// <see cref="EscalatedOptions.TicketActions"/>). Host applications may replace
/// the default registration with their own implementation for dynamic
/// per-ticket/user visibility.
/// </summary>
public interface ITicketActionRegistry
{
    /// <summary>Find a configured action by key, or null.</summary>
    TicketActionConfig? Find(string key);

    /// <summary>
    /// The visible actions for a ticket/user, serialized for the UI. The
    /// controller adds the <c>url</c> and <c>method</c> before responding.
    /// </summary>
    IReadOnlyList<Dictionary<string, object?>> ForTicket(Ticket ticket, string? userId);
}

/// <inheritdoc />
public class TicketActionRegistry : ITicketActionRegistry
{
    private readonly List<TicketActionConfig> _actions;

    public TicketActionRegistry(IOptions<EscalatedOptions> options)
    {
        _actions = (options.Value.TicketActions ?? new List<TicketActionConfig>())
            .Where(a => !string.IsNullOrEmpty(a.Key) && !string.IsNullOrEmpty(a.Label))
            .ToList();
    }

    public TicketActionConfig? Find(string key) =>
        _actions.FirstOrDefault(a => a.Key == key);

    public IReadOnlyList<Dictionary<string, object?>> ForTicket(Ticket ticket, string? userId) =>
        _actions
            .Where(a => a.Visible)
            .Select(a => new Dictionary<string, object?>
            {
                ["key"] = a.Key,
                ["label"] = a.Label,
                ["variant"] = a.Variant,
                ["confirmation"] = a.Confirmation,
                ["disabled"] = !a.Enabled,
                ["metadata"] = a.Metadata ?? new Dictionary<string, object>(),
            })
            .ToList();
}
