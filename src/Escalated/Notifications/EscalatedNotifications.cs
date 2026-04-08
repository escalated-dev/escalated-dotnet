using Escalated.Models;

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
/// Email message builder for Escalated notifications.
/// Provides branded email templates with In-Reply-To/References/Message-ID headers for threading.
/// </summary>
public static class EmailTemplates
{
    public static EmailMessage NewTicket(Ticket ticket, string supportEmail)
    {
        return new EmailMessage
        {
            Subject = $"[{ticket.Reference}] {ticket.Subject}",
            Body = $"<h2>New Ticket: {ticket.Subject}</h2><p>{ticket.Description}</p>",
            MessageId = $"<{ticket.Reference}@escalated>",
            ToEmail = supportEmail
        };
    }

    public static EmailMessage TicketReply(Ticket ticket, Reply reply, string recipientEmail)
    {
        return new EmailMessage
        {
            Subject = $"Re: [{ticket.Reference}] {ticket.Subject}",
            Body = $"<p>{reply.Body}</p>",
            MessageId = $"<{ticket.Reference}-reply-{reply.Id}@escalated>",
            InReplyTo = $"<{ticket.Reference}@escalated>",
            References = $"<{ticket.Reference}@escalated>",
            ToEmail = recipientEmail
        };
    }

    public static EmailMessage SlaBreachAlert(Ticket ticket, string breachType, string recipientEmail)
    {
        return new EmailMessage
        {
            Subject = $"SLA Breach: [{ticket.Reference}] {ticket.Subject}",
            Body = $"<h2>SLA {breachType} Breached</h2><p>Ticket {ticket.Reference} has breached its {breachType} SLA target.</p>",
            MessageId = $"<{ticket.Reference}-sla-{breachType}@escalated>",
            InReplyTo = $"<{ticket.Reference}@escalated>",
            ToEmail = recipientEmail
        };
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
}
