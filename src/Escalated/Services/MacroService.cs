using System.Text.Json;
using Escalated.Enums;
using Escalated.Models;

namespace Escalated.Services;

public class MacroService
{
    private readonly TicketService _ticketService;
    private readonly AssignmentService _assignmentService;

    public MacroService(TicketService ticketService, AssignmentService assignmentService)
    {
        _ticketService = ticketService;
        _assignmentService = assignmentService;
    }

    /// <summary>
    /// Apply a macro's actions to a ticket.
    /// </summary>
    public async Task<Ticket> ApplyAsync(Macro macro, Ticket ticket, int causerId,
        CancellationToken ct = default)
    {
        var actions = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(macro.Actions)
            ?? new List<Dictionary<string, string>>();

        foreach (var action in actions)
        {
            var type = action.GetValueOrDefault("type", "");
            var value = action.GetValueOrDefault("value", "");

            switch (type)
            {
                case "status":
                    ticket = await _ticketService.ChangeStatusAsync(
                        ticket, TicketStatusExtensions.Parse(value), causerId, ct);
                    break;
                case "priority":
                    ticket = await _ticketService.ChangePriorityAsync(
                        ticket, TicketPriorityExtensions.Parse(value), causerId, ct);
                    break;
                case "assign":
                    ticket = await _assignmentService.AssignAsync(
                        ticket, int.Parse(value), causerId, ct);
                    break;
                case "tags":
                    var tagIds = JsonSerializer.Deserialize<int[]>(value) ?? Array.Empty<int>();
                    ticket = await _ticketService.AddTagsAsync(ticket, tagIds, causerId, ct);
                    break;
                case "department":
                    ticket = await _ticketService.ChangeDepartmentAsync(
                        ticket, int.Parse(value), causerId, ct);
                    break;
                case "reply":
                    await _ticketService.AddReplyAsync(ticket, value, causerId, null, false, ct);
                    break;
                case "note":
                    await _ticketService.AddReplyAsync(ticket, value, causerId, null, true, ct);
                    break;
            }
        }

        return ticket;
    }
}
