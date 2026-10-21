using System.Net;
using System.Net.Mail;
using Escalated.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Escalated.Notifications;

/// <summary>
/// Delivers a built <see cref="EmailMessage"/>. The default,
/// <see cref="SmtpMailTransport"/>, sends through the SMTP server in
/// <c>Escalated:Email:Smtp</c>; register your own to send another way (an email
/// API, a queue).
/// </summary>
public interface IEscalatedMailTransport
{
    /// <summary>Whether this transport can send. When false, notification emails are skipped.</summary>
    bool IsConfigured { get; }

    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}

/// <summary>
/// Sends through <see cref="SmtpOptions"/> with <see cref="SmtpClient"/>, keeping the
/// threading headers (<c>Message-ID</c>, <c>In-Reply-To</c>, <c>References</c>) and
/// the signed <c>Reply-To</c>. Without <see cref="SmtpOptions.Host"/> it is not
/// configured: nothing is sent, and that is logged once.
/// </summary>
public sealed class SmtpMailTransport : IEscalatedMailTransport
{
    private readonly IOptionsMonitor<EscalatedOptions> _options;
    private readonly ILogger<SmtpMailTransport> _logger;
    private int _reportedOff;

    public SmtpMailTransport(IOptionsMonitor<EscalatedOptions> options, ILogger<SmtpMailTransport> logger)
    {
        _options = options;
        _logger = logger;
    }

    public bool IsConfigured
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_options.CurrentValue.Email.Smtp.Host)) return true;

            if (Interlocked.Exchange(ref _reportedOff, 1) == 0)
            {
                _logger.LogInformation(
                    "Escalated notification emails are off: set Escalated:Email:Smtp:Host, or register an IEscalatedMailTransport or IEscalatedNotificationSender.");
            }

            return false;
        }
    }

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (!IsConfigured) return;

        var email = _options.CurrentValue.Email;
        var smtp = email.Smtp;

        using var mail = new MailMessage
        {
            From = new MailAddress(FromAddress(email), email.FromName),
            Subject = message.Subject,
            Body = message.Body,
            IsBodyHtml = true,
        };
        mail.To.Add(new MailAddress(message.ToEmail));
        if (!string.IsNullOrEmpty(message.ReplyTo)) mail.ReplyToList.Add(new MailAddress(message.ReplyTo));
        if (!string.IsNullOrEmpty(message.MessageId)) mail.Headers["Message-ID"] = message.MessageId;
        if (!string.IsNullOrEmpty(message.InReplyTo)) mail.Headers["In-Reply-To"] = message.InReplyTo;
        if (!string.IsNullOrEmpty(message.References)) mail.Headers["References"] = message.References;

        using var client = new SmtpClient(smtp.Host!, smtp.Port)
        {
            EnableSsl = smtp.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = Math.Max(1, smtp.TimeoutSeconds) * 1000,
        };
        if (!string.IsNullOrEmpty(smtp.Username))
        {
            client.Credentials = new NetworkCredential(smtp.Username, smtp.Password);
        }

        await client.SendMailAsync(mail, ct);
    }

    internal static string FromAddress(EmailOptions email) =>
        string.IsNullOrWhiteSpace(email.FromAddress) ? $"support@{email.Domain}" : email.FromAddress!;
}
