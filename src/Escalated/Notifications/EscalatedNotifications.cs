using Escalated.Configuration;
using Escalated.Models;
using Escalated.Services.Email;

namespace Escalated.Notifications;

/// <summary>
/// Sends Escalated's notifications. Escalated calls it for every ticket event
/// that notifies someone (see <c>NotificationEventDispatcher</c>). The default,
/// <see cref="EmailNotificationSender"/>, picks the recipients the way the Laravel
/// reference does and emails them through <see cref="IEscalatedMailTransport"/>.
/// Host apps register their own implementation to notify another way.
/// </summary>
public interface IEscalatedNotificationSender
{
    Task SendNewTicketNotificationAsync(Ticket ticket, CancellationToken ct = default);
    Task SendReplyNotificationAsync(Reply reply, Ticket ticket, CancellationToken ct = default);

    /// <summary>
    /// Notifies a host user that they were @-mentioned in an internal note.
    /// Mirrors the Laravel reference <c>MentionNotification</c>: the mentioned
    /// agent is told who mentioned them and on which ticket.
    /// </summary>
    Task SendMentionNotificationAsync(Reply reply, Ticket ticket, string mentionedUserId, CancellationToken ct = default);

    Task SendAssignmentNotificationAsync(Ticket ticket, string agentId, CancellationToken ct = default);

    /// <summary>As <see cref="SendAssignmentNotificationAsync(Ticket, string, CancellationToken)"/>, knowing who assigned it.</summary>
    Task SendAssignmentNotificationAsync(Ticket ticket, string agentId, string? causerId, CancellationToken ct = default)
        => SendAssignmentNotificationAsync(ticket, agentId, ct);

    Task SendStatusChangeNotificationAsync(Ticket ticket, string oldStatus, string newStatus, CancellationToken ct = default);

    /// <summary>As <see cref="SendStatusChangeNotificationAsync(Ticket, string, string, CancellationToken)"/>, knowing who changed it.</summary>
    Task SendStatusChangeNotificationAsync(Ticket ticket, string oldStatus, string newStatus, string? causerId, CancellationToken ct = default)
        => SendStatusChangeNotificationAsync(ticket, oldStatus, newStatus, ct);
    Task SendSlaBreachNotificationAsync(Ticket ticket, string breachType, CancellationToken ct = default);
    Task SendEscalationNotificationAsync(Ticket ticket, CancellationToken ct = default);
    Task SendResolvedNotificationAsync(Ticket ticket, CancellationToken ct = default);
}

/// <summary>
/// Sends nothing. Register it to switch notifications off whatever the email settings.
/// </summary>
public class NullNotificationSender : IEscalatedNotificationSender
{
    public Task SendNewTicketNotificationAsync(Ticket ticket, CancellationToken ct) => Task.CompletedTask;
    public Task SendReplyNotificationAsync(Reply reply, Ticket ticket, CancellationToken ct) => Task.CompletedTask;
    public Task SendMentionNotificationAsync(Reply reply, Ticket ticket, string mentionedUserId, CancellationToken ct) => Task.CompletedTask;
    public Task SendAssignmentNotificationAsync(Ticket ticket, string agentId, CancellationToken ct) => Task.CompletedTask;
    public Task SendStatusChangeNotificationAsync(Ticket ticket, string oldStatus, string newStatus, CancellationToken ct) => Task.CompletedTask;
    public Task SendSlaBreachNotificationAsync(Ticket ticket, string breachType, CancellationToken ct) => Task.CompletedTask;
    public Task SendEscalationNotificationAsync(Ticket ticket, CancellationToken ct) => Task.CompletedTask;
    public Task SendResolvedNotificationAsync(Ticket ticket, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>
/// Email message builder for Escalated notifications. Provides branded
/// email templates with RFC 5322 threading headers (Message-ID /
/// In-Reply-To / References) plus a signed Reply-To so inbound provider
/// webhooks can verify ticket identity.
///
/// Every method takes <see cref="EmailOptions"/> so the <c>Domain</c>
/// and <c>InboundSecret</c> don't have to be baked into call sites.
/// Host apps with the default DI wiring resolve <c>IOptions&lt;EscalatedOptions&gt;</c>
/// and pass <c>options.Value.Email</c>.
/// </summary>
public static class EmailTemplates
{
    public static EmailMessage NewTicket(Ticket ticket, string supportEmail, EmailOptions email)
    {
        return new EmailMessage
        {
            Subject = $"[{ticket.Reference}] {ticket.Subject}",
            Body = $"<h2>New Ticket: {ticket.Subject}</h2><p>{ticket.Description}</p>",
            MessageId = MessageIdUtil.BuildMessageId(ticket.Id, null, email.Domain),
            ToEmail = supportEmail,
            ReplyTo = SignedReplyTo(ticket, email),
        };
    }

    public static EmailMessage TicketReply(Ticket ticket, Reply reply, string recipientEmail, EmailOptions email)
    {
        return new EmailMessage
        {
            Subject = $"Re: [{ticket.Reference}] {ticket.Subject}",
            Body = $"<p>{reply.Body}</p>",
            MessageId = MessageIdUtil.BuildMessageId(ticket.Id, reply.Id, email.Domain),
            InReplyTo = MessageIdUtil.BuildMessageId(ticket.Id, null, email.Domain),
            References = MessageIdUtil.BuildMessageId(ticket.Id, null, email.Domain),
            ToEmail = recipientEmail,
            ReplyTo = SignedReplyTo(ticket, email),
        };
    }

    /// <summary>
    /// Any other notice about a ticket: threaded to the ticket's anchor message so
    /// it lands in the same conversation. <paramref name="html"/> must already be
    /// encoded; use <see cref="Encode"/> for ticket fields.
    /// </summary>
    public static EmailMessage Notice(Ticket ticket, string subject, string html, string recipientEmail, EmailOptions email)
    {
        var anchor = MessageIdUtil.BuildMessageId(ticket.Id, null, email.Domain);
        return new EmailMessage
        {
            Subject = subject,
            Body = html,
            InReplyTo = anchor,
            References = anchor,
            ToEmail = recipientEmail,
            ReplyTo = SignedReplyTo(ticket, email),
        };
    }

    public static EmailMessage Assigned(Ticket ticket, string recipientEmail, EmailOptions email) =>
        Notice(ticket, $"Assigned to you: [{ticket.Reference}] {ticket.Subject}",
            $"<p>Ticket <strong>{Encode(ticket.Reference)}</strong> ({Encode(ticket.Subject)}) has been assigned to you.</p>",
            recipientEmail, email);

    public static EmailMessage StatusChanged(Ticket ticket, string oldStatus, string newStatus, string recipientEmail, EmailOptions email) =>
        Notice(ticket, $"[{ticket.Reference}] status changed to {newStatus}",
            $"<p>The status of ticket <strong>{Encode(ticket.Reference)}</strong> ({Encode(ticket.Subject)}) changed from {Encode(oldStatus)} to {Encode(newStatus)}.</p>",
            recipientEmail, email);

    public static EmailMessage TicketEscalated(Ticket ticket, string recipientEmail, EmailOptions email) =>
        Notice(ticket, $"Escalated: [{ticket.Reference}] {ticket.Subject}",
            $"<p>Ticket <strong>{Encode(ticket.Reference)}</strong> ({Encode(ticket.Subject)}) has been escalated.</p>",
            recipientEmail, email);

    public static EmailMessage Mention(Ticket ticket, Reply note, string recipientEmail, EmailOptions email) =>
        Notice(ticket, $"You were mentioned on [{ticket.Reference}] {ticket.Subject}",
            $"<p>You were mentioned in an internal note on ticket <strong>{Encode(ticket.Reference)}</strong>:</p><blockquote>{Encode(note.Body)}</blockquote>",
            recipientEmail, email);

    /// <summary>HTML-encodes a ticket field for an email body.</summary>
    public static string Encode(string? value) => System.Net.WebUtility.HtmlEncode(value ?? string.Empty);

    public static EmailMessage SlaBreachAlert(Ticket ticket, string breachType, string recipientEmail, EmailOptions email)
    {
        return new EmailMessage
        {
            Subject = $"SLA Breach: [{ticket.Reference}] {ticket.Subject}",
            Body = $"<h2>SLA {breachType} Breached</h2><p>Ticket {ticket.Reference} has breached its {breachType} SLA target.</p>",
            InReplyTo = MessageIdUtil.BuildMessageId(ticket.Id, null, email.Domain),
            References = MessageIdUtil.BuildMessageId(ticket.Id, null, email.Domain),
            ToEmail = recipientEmail,
            ReplyTo = SignedReplyTo(ticket, email),
        };
    }

    private static string? SignedReplyTo(Ticket ticket, EmailOptions email)
    {
        return string.IsNullOrEmpty(email.InboundSecret)
            ? null
            : MessageIdUtil.BuildReplyTo(ticket.Id, email.InboundSecret, email.Domain);
    }
}

public class EmailMessage
{
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string ToEmail { get; set; } = string.Empty;
    public string? MessageId { get; set; }
    public string? InReplyTo { get; set; }
    public string? References { get; set; }

    /// <summary>
    /// Signed Reply-To (<c>reply+{id}.{hmac8}@{domain}</c>) so inbound
    /// provider webhooks can verify ticket identity even when the mail
    /// client strips the Message-ID chain. Null when no inbound secret
    /// is configured.
    /// </summary>
    public string? ReplyTo { get; set; }
}
