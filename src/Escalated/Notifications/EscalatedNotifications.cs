using Escalated.Configuration;
using Escalated.Models;
using Escalated.Services.Email;

namespace Escalated.Notifications;

/// <summary>
/// Interface for sending Escalated notifications. Host apps implement this
/// to integrate with their email/notification system.
/// </summary>
public interface IEscalatedNotificationSender
{
    Task SendNewTicketNotificationAsync(Ticket ticket, CancellationToken ct = default);
    Task SendReplyNotificationAsync(Reply reply, Ticket ticket, CancellationToken ct = default);
    Task SendAssignmentNotificationAsync(Ticket ticket, int agentId, CancellationToken ct = default);
    Task SendStatusChangeNotificationAsync(Ticket ticket, string oldStatus, string newStatus, CancellationToken ct = default);
    Task SendSlaBreachNotificationAsync(Ticket ticket, string breachType, CancellationToken ct = default);
    Task SendEscalationNotificationAsync(Ticket ticket, CancellationToken ct = default);
    Task SendResolvedNotificationAsync(Ticket ticket, CancellationToken ct = default);
}

/// <summary>
/// No-op default implementation. Host apps register their own sender.
/// </summary>
public class NullNotificationSender : IEscalatedNotificationSender
{
    public Task SendNewTicketNotificationAsync(Ticket ticket, CancellationToken ct) => Task.CompletedTask;
    public Task SendReplyNotificationAsync(Reply reply, Ticket ticket, CancellationToken ct) => Task.CompletedTask;
    public Task SendAssignmentNotificationAsync(Ticket ticket, int agentId, CancellationToken ct) => Task.CompletedTask;
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
