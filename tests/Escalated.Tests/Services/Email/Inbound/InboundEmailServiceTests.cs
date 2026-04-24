using Escalated.Configuration;
using Escalated.Enums;
using Escalated.Models;
using Escalated.Services;
using Escalated.Services.Email.Inbound;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Escalated.Tests.Services.Email.Inbound;

/// <summary>
/// Tests for <see cref="InboundEmailService.ProcessAsync"/>:
/// parser → router → reply/create/skip orchestration.
/// Uses in-memory EF Core + real TicketService so the DB state
/// after each call reflects the actual wiring.
/// </summary>
public class InboundEmailServiceTests
{
    private static (InboundEmailService svc, Data.EscalatedDbContext db, EscalatedOptions options)
        Create(string? secret = null)
    {
        var db = TestHelpers.CreateInMemoryDb();
        var options = new EscalatedOptions
        {
            Email = new EmailOptions
            {
                Domain = "support.example.com",
                InboundSecret = secret ?? string.Empty,
            },
        };
        var events = TestHelpers.MockEventDispatcher();
        var tickets = new TicketService(
            db, events.Object,
            Microsoft.Extensions.Options.Options.Create(options));
        var router = new InboundEmailRouter(db, options);
        var svc = new InboundEmailService(
            db, tickets, router,
            NullLogger<InboundEmailService>.Instance);
        return (svc, db, options);
    }

    private static async Task<Ticket> SeedTicket(Data.EscalatedDbContext db, int id = 42)
    {
        var ticket = new Ticket
        {
            Reference = $"ESC-{id:00000}",
            Subject = "Existing",
            Status = TicketStatus.Open,
            Priority = TicketPriority.Medium,
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();
        return ticket;
    }

    private static InboundEmail SeedInboundAudit(Data.EscalatedDbContext db)
    {
        var row = new InboundEmail
        {
            FromEmail = "c@example.com",
            Status = "pending",
        };
        db.InboundEmails.Add(row);
        db.SaveChanges();
        return row;
    }

    private static InboundMessage MakeMessage(
        string? inReplyTo = null,
        string? toEmail = "support@example.com",
        string subject = "hello",
        string? body = "body",
        string fromEmail = "customer@example.com",
        string? fromName = "Customer")
        => new()
        {
            FromEmail = fromEmail,
            FromName = fromName,
            ToEmail = toEmail ?? string.Empty,
            Subject = subject,
            BodyText = body,
            InReplyTo = inReplyTo,
        };

    [Fact]
    public async Task ProcessAsync_ExistingTicketMatched_AddsReplyAndReturnsRepliedOutcome()
    {
        var (svc, db, _) = Create();
        var ticket = await SeedTicket(db);
        var audit = SeedInboundAudit(db);
        var message = MakeMessage(inReplyTo: $"<ticket-{ticket.Id}@support.example.com>");

        var result = await svc.ProcessAsync(message, audit);

        Assert.Equal(ProcessOutcome.RepliedToExisting, result.Outcome);
        Assert.Equal(ticket.Id, result.TicketId);
        Assert.NotNull(result.ReplyId);
        Assert.Equal("replied", audit.Status);
        Assert.Equal(ticket.Id, audit.TicketId);
        Assert.Equal(result.ReplyId, audit.ReplyId);
        // Reply was actually persisted.
        Assert.Single(db.Replies);
    }

    [Fact]
    public async Task ProcessAsync_NoMatchAndRealContent_CreatesNewTicket()
    {
        var (svc, db, _) = Create();
        var audit = SeedInboundAudit(db);
        var message = MakeMessage(subject: "New issue", body: "Actual problem");

        var result = await svc.ProcessAsync(message, audit);

        Assert.Equal(ProcessOutcome.CreatedNew, result.Outcome);
        Assert.NotNull(result.TicketId);
        Assert.Null(result.ReplyId);
        Assert.Equal("created", audit.Status);
        Assert.Equal(result.TicketId, audit.TicketId);

        var newTicket = db.Tickets.Single(t => t.Id == result.TicketId);
        Assert.Equal("New issue", newTicket.Subject);
        Assert.Equal("customer@example.com", newTicket.GuestEmail);
        Assert.Equal("Customer", newTicket.GuestName);
    }

    [Fact]
    public async Task ProcessAsync_NoSubjectFallsBackToPlaceholder()
    {
        var (svc, db, _) = Create();
        var audit = SeedInboundAudit(db);
        var message = MakeMessage(subject: "", body: "Has content, missing subject though");

        var result = await svc.ProcessAsync(message, audit);

        Assert.Equal(ProcessOutcome.CreatedNew, result.Outcome);
        var newTicket = db.Tickets.Single(t => t.Id == result.TicketId);
        Assert.Equal("(no subject)", newTicket.Subject);
    }

    [Fact]
    public async Task ProcessAsync_SkipsSnsConfirmation()
    {
        var (svc, db, _) = Create();
        var audit = SeedInboundAudit(db);
        var message = MakeMessage(fromEmail: "no-reply@sns.amazonaws.com", subject: "SubscriptionConfirmation");

        var result = await svc.ProcessAsync(message, audit);

        Assert.Equal(ProcessOutcome.Skipped, result.Outcome);
        Assert.Null(result.TicketId);
        Assert.Equal("skipped", audit.Status);
        Assert.Empty(db.Tickets);
    }

    [Fact]
    public async Task ProcessAsync_SkipsEmptyBodyAndSubject()
    {
        var (svc, db, _) = Create();
        var audit = SeedInboundAudit(db);
        var message = MakeMessage(subject: "", body: "");

        var result = await svc.ProcessAsync(message, audit);

        Assert.Equal(ProcessOutcome.Skipped, result.Outcome);
        Assert.Empty(db.Tickets);
    }

    [Fact]
    public async Task ProcessAsync_PassesThroughPendingAttachmentDownloads()
    {
        var (svc, db, _) = Create();
        var audit = SeedInboundAudit(db);
        var message = new InboundMessage
        {
            FromEmail = "customer@example.com",
            ToEmail = "support@example.com",
            Subject = "With attachments",
            BodyText = "See attached",
            Attachments = new[]
            {
                new InboundAttachment
                {
                    Name = "large.pdf",
                    ContentType = "application/pdf",
                    SizeBytes = 10_000_000,
                    DownloadUrl = "https://mailgun.example/att/large",
                },
                new InboundAttachment
                {
                    Name = "inline.txt",
                    ContentType = "text/plain",
                    Content = System.Text.Encoding.UTF8.GetBytes("hello"),
                },
            },
        };

        var result = await svc.ProcessAsync(message, audit);

        Assert.Single(result.PendingAttachmentDownloads);
        var pending = result.PendingAttachmentDownloads[0];
        Assert.Equal("large.pdf", pending.Name);
        Assert.Equal("https://mailgun.example/att/large", pending.DownloadUrl);
    }

    [Fact]
    public void IsNoiseEmail_ReturnsTrueForSnsConfirmation()
    {
        Assert.True(InboundEmailService.IsNoiseEmail(new InboundMessage
        {
            FromEmail = "no-reply@sns.amazonaws.com",
            ToEmail = "to@example.com",
            Subject = "SubscriptionConfirmation",
        }));
    }

    [Fact]
    public void IsNoiseEmail_ReturnsTrueForEmptyBodyAndSubject()
    {
        Assert.True(InboundEmailService.IsNoiseEmail(new InboundMessage
        {
            FromEmail = "customer@example.com",
            ToEmail = "support@example.com",
            Subject = "",
        }));
    }

    [Fact]
    public void IsNoiseEmail_ReturnsFalseForRealEmail()
    {
        Assert.False(InboundEmailService.IsNoiseEmail(new InboundMessage
        {
            FromEmail = "customer@example.com",
            ToEmail = "support@example.com",
            Subject = "Real issue",
            BodyText = "real content",
        }));
    }
}
