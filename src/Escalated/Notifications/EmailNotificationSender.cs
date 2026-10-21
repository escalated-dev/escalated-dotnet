using Escalated.Configuration;
using Escalated.Data;
using Escalated.Models;
using Escalated.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Escalated.Notifications;

/// <summary>
/// The default <see cref="IEscalatedNotificationSender"/>: emails the people the
/// Laravel reference's notification listeners notify, built with
/// <see cref="EmailTemplates"/> (threaded on the ticket's anchor Message-ID, with a
/// signed Reply-To when <see cref="EmailOptions.InboundSecret"/> is set) and sent
/// through <see cref="IEscalatedMailTransport"/>.
///
/// <list type="bullet">
///   <item>New ticket: the requester, and the assignee when it is created assigned.</item>
///   <item>Public reply: the requester unless they wrote it, the assignee unless they
///   wrote it, and followers other than the author, requester and assignee. Internal
///   notes email nobody.</item>
///   <item>Assigned: the agent, unless they assigned themselves.</item>
///   <item>Status changed: the requester and followers, except whoever changed it.</item>
///   <item>Escalated, SLA breached: the assignee.</item>
///   <item>Mentioned: the mentioned user.</item>
/// </list>
///
/// The requester's address is the guest email, the ticket's contact, or the host
/// user's email from <see cref="IUserDirectory"/>; agents and followers come from
/// <see cref="IUserDirectory"/>. Nobody is emailed about their own action. A failed
/// delivery is logged and never fails the change that caused it.
/// </summary>
public class EmailNotificationSender : IEscalatedNotificationSender
{
    private readonly IEscalatedMailTransport _transport;
    private readonly IUserDirectory _users;
    private readonly EscalatedDbContext _db;
    private readonly EscalatedOptions _options;
    private readonly ILogger<EmailNotificationSender> _logger;

    public EmailNotificationSender(
        IEscalatedMailTransport transport,
        IUserDirectory users,
        EscalatedDbContext db,
        IOptions<EscalatedOptions> options,
        ILogger<EmailNotificationSender> logger)
    {
        _transport = transport;
        _users = users;
        _db = db;
        _options = options.Value;
        _logger = logger;
    }

    private EmailOptions Email => _options.Email;

    public async Task SendNewTicketNotificationAsync(Ticket ticket, CancellationToken ct = default)
    {
        if (!_transport.IsConfigured) return;

        var recipients = new Recipients();
        recipients.Add(await RequesterEmailAsync(ticket, ct));
        if (!string.IsNullOrEmpty(ticket.AssignedTo)) recipients.Add(await UserEmailAsync(ticket.AssignedTo, ct));

        foreach (var to in recipients) await SendAsync(EmailTemplates.NewTicket(ticket, to, Email), ct);
    }

    public async Task SendReplyNotificationAsync(Reply reply, Ticket ticket, CancellationToken ct = default)
    {
        if (reply.IsInternalNote || !_transport.IsConfigured) return;

        var authorIsRequester = IsRequesterAuthored(reply, ticket);
        var recipients = new Recipients();

        if (!authorIsRequester) recipients.Add(await RequesterEmailAsync(ticket, ct));

        if (!string.IsNullOrEmpty(ticket.AssignedTo) && reply.AuthorId != ticket.AssignedTo)
            recipients.Add(await UserEmailAsync(ticket.AssignedTo, ct));

        foreach (var follower in await FollowerIdsAsync(ticket.Id, ct))
        {
            if (follower == reply.AuthorId || follower == ticket.RequesterId || follower == ticket.AssignedTo) continue;
            recipients.Add(await UserEmailAsync(follower, ct));
        }

        foreach (var to in recipients) await SendAsync(EmailTemplates.TicketReply(ticket, reply, to, Email), ct);
    }

    public async Task SendMentionNotificationAsync(Reply reply, Ticket ticket, string mentionedUserId, CancellationToken ct = default)
    {
        if (!_transport.IsConfigured || mentionedUserId == reply.AuthorId) return;

        var to = await UserEmailAsync(mentionedUserId, ct);
        if (to is not null) await SendAsync(EmailTemplates.Mention(ticket, reply, to, Email), ct);
    }

    public Task SendAssignmentNotificationAsync(Ticket ticket, string agentId, CancellationToken ct = default) =>
        SendAssignmentNotificationAsync(ticket, agentId, null, ct);

    public async Task SendAssignmentNotificationAsync(Ticket ticket, string agentId, string? causerId, CancellationToken ct = default)
    {
        if (!_transport.IsConfigured || agentId == causerId) return;

        var to = await UserEmailAsync(agentId, ct);
        if (to is not null) await SendAsync(EmailTemplates.Assigned(ticket, to, Email), ct);
    }

    public Task SendStatusChangeNotificationAsync(Ticket ticket, string oldStatus, string newStatus, CancellationToken ct = default) =>
        SendStatusChangeNotificationAsync(ticket, oldStatus, newStatus, null, ct);

    public async Task SendStatusChangeNotificationAsync(Ticket ticket, string oldStatus, string newStatus, string? causerId, CancellationToken ct = default)
    {
        if (!_transport.IsConfigured) return;

        var recipients = new Recipients();
        if (causerId is null || causerId != ticket.RequesterId) recipients.Add(await RequesterEmailAsync(ticket, ct));

        foreach (var follower in await FollowerIdsAsync(ticket.Id, ct))
        {
            if (follower == causerId || follower == ticket.RequesterId) continue;
            recipients.Add(await UserEmailAsync(follower, ct));
        }

        foreach (var to in recipients) await SendAsync(EmailTemplates.StatusChanged(ticket, oldStatus, newStatus, to, Email), ct);
    }

    public async Task SendSlaBreachNotificationAsync(Ticket ticket, string breachType, CancellationToken ct = default)
    {
        if (!_transport.IsConfigured || string.IsNullOrEmpty(ticket.AssignedTo)) return;

        var to = await UserEmailAsync(ticket.AssignedTo, ct);
        if (to is not null) await SendAsync(EmailTemplates.SlaBreachAlert(ticket, breachType, to, Email), ct);
    }

    public async Task SendEscalationNotificationAsync(Ticket ticket, CancellationToken ct = default)
    {
        if (!_transport.IsConfigured || string.IsNullOrEmpty(ticket.AssignedTo)) return;

        var to = await UserEmailAsync(ticket.AssignedTo, ct);
        if (to is not null) await SendAsync(EmailTemplates.TicketEscalated(ticket, to, Email), ct);
    }

    /// <summary>Nothing extra: the status-change email already tells the requester.</summary>
    public Task SendResolvedNotificationAsync(Ticket ticket, CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>
    /// A reply the requester wrote: by their host user id, or a guest's (no author id,
    /// and not a system reply such as a Workflow's canned reply).
    /// </summary>
    private static bool IsRequesterAuthored(Reply reply, Ticket ticket)
    {
        if (!string.IsNullOrEmpty(reply.AuthorId)) return reply.AuthorId == ticket.RequesterId;
        return reply.AuthorType != "system";
    }

    private async Task<string?> RequesterEmailAsync(Ticket ticket, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(ticket.GuestEmail)) return ticket.GuestEmail;

        if (ticket.ContactId is { } contactId)
        {
            var contact = await _db.Contacts.AsNoTracking().Where(c => c.Id == contactId).Select(c => c.Email).FirstOrDefaultAsync(ct);
            if (!string.IsNullOrWhiteSpace(contact)) return contact;
        }

        return string.IsNullOrEmpty(ticket.RequesterId) ? null : await UserEmailAsync(ticket.RequesterId, ct);
    }

    private async Task<string?> UserEmailAsync(string userId, CancellationToken ct)
    {
        var user = await _users.FindAsync(userId, ct);
        return string.IsNullOrWhiteSpace(user?.Email) ? null : user.Email;
    }

    private Task<List<string>> FollowerIdsAsync(int ticketId, CancellationToken ct) =>
        _db.TicketFollowers.AsNoTracking().Where(f => f.TicketId == ticketId).Select(f => f.UserId).ToListAsync(ct);

    private async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        try
        {
            await _transport.SendAsync(message, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "[EmailNotificationSender] sending \"{Subject}\" failed", message.Subject);
        }
    }

    /// <summary>Distinct, non-empty addresses in the order added.</summary>
    private sealed class Recipients : List<string>
    {
        public new void Add(string? email)
        {
            if (string.IsNullOrWhiteSpace(email)) return;
            if (this.Any(e => string.Equals(e, email, StringComparison.OrdinalIgnoreCase))) return;
            base.Add(email);
        }
    }
}
