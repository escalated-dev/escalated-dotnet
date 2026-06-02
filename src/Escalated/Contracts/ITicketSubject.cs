namespace Escalated.Contracts;

/// <summary>
/// Host-app model contract for entities a ticket can be <em>about</em> (Project,
/// Customer, asset, …), distinct from the requester. Register a
/// <see cref="Services.ITicketSubjectResolver"/> to resolve stored type/id pairs.
/// </summary>
public interface ITicketSubject
{
    string TicketSubjectTitle();

    string? TicketSubjectSubtitle();

    string? TicketSubjectUrl();

    string? TicketSubjectColor();

    string? TicketSubjectIcon();
}
