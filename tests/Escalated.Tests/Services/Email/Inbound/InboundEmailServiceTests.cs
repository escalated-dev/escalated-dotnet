using Escalated.Configuration;
using Escalated.Enums;
using Escalated.Models;
using Escalated.Services;
using Escalated.Services.Email;
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
        Create(string? secret = null, IUserDirectory? users = null)
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
            NullLogger<InboundEmailService>.Instance,
            users);
        return (svc, db, options);
    }

    private static async Task<Ticket> SeedTicket(
        Data.EscalatedDbContext db,
        int id = 42,
        string? guestEmail = "customer@example.com",
        TicketStatus status = TicketStatus.Open,
        string? requesterId = null)
    {
        var ticket = new Ticket
        {
            Reference = $"ESC-{id:00000}",
            Subject = "Existing",
            Status = status,
            Priority = TicketPriority.Medium,
            GuestEmail = guestEmail,
            RequesterId = requesterId,
            RequesterType = requesterId is null ? null : "User",
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

    private sealed class FakeUserDirectory : IUserDirectory
    {
        private readonly Dictionary<string, UserDirectoryEntry> _users;

        public FakeUserDirectory(params UserDirectoryEntry[] users)
            => _users = users.ToDictionary(u => u.Id);

        public Task<UserDirectoryPage> ListAsync(string? search, int page, int pageSize, CancellationToken ct = default)
            => Task.FromResult(new UserDirectoryPage(_users.Values.ToList(), _users.Count, page, pageSize));

        public Task<UserDirectoryEntry?> FindAsync(string id, CancellationToken ct = default)
            => Task.FromResult(_users.TryGetValue(id, out var u) ? u : null);
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
    public async Task ProcessAsync_StrangerQuotingSubjectReference_OpensNewTicket()
    {
        var (svc, db, _) = Create();
        var ticket = await SeedTicket(db, id: 7001, guestEmail: "owner@example.com");
        var audit = SeedInboundAudit(db);
        var message = MakeMessage(
            subject: $"RE: [{ticket.Reference}] Your order",
            body: "Injected reply.",
            fromEmail: "stranger@example.net");

        var result = await svc.ProcessAsync(message, audit);

        Assert.Equal(ProcessOutcome.CreatedNew, result.Outcome);
        Assert.NotEqual(ticket.Id, result.TicketId);
        Assert.Null(result.ReplyId);
        Assert.Empty(db.Replies.Where(r => r.TicketId == ticket.Id));
        Assert.Equal("stranger@example.net", db.Tickets.Single(t => t.Id == result.TicketId).GuestEmail);
    }

    [Fact]
    public async Task ProcessAsync_StrangerThreadingOntoClosedTicket_DoesNotReopenIt()
    {
        var (svc, db, _) = Create();
        var ticket = await SeedTicket(db, guestEmail: "owner@example.com", status: TicketStatus.Closed);
        var audit = SeedInboundAudit(db);
        var message = MakeMessage(
            inReplyTo: $"<ticket-{ticket.Id}@support.example.com>",
            subject: $"RE: [{ticket.Reference}] Closed",
            body: "Reopen this.",
            fromEmail: "stranger@example.net");

        var result = await svc.ProcessAsync(message, audit);

        Assert.Equal(ProcessOutcome.CreatedNew, result.Outcome);
        Assert.NotEqual(ticket.Id, result.TicketId);
        Assert.Equal(TicketStatus.Closed, db.Tickets.Single(t => t.Id == ticket.Id).Status);
        Assert.Empty(db.Replies.Where(r => r.TicketId == ticket.Id));
    }

    [Fact]
    public async Task ProcessAsync_FromHeaderNamingAnAgent_IsNeverPostedAsThatAgent()
    {
        var users = new FakeUserDirectory(new UserDirectoryEntry("agent-1", "Agent", "agent@example.com"));
        var (svc, db, options) = Create("test-secret", users);
        var ticket = await SeedTicket(db, guestEmail: "owner@example.com");
        var audit = SeedInboundAudit(db);
        var message = MakeMessage(
            inReplyTo: $"<ticket-{ticket.Id}@support.example.com>",
            toEmail: MessageIdUtil.BuildReplyTo(ticket.Id, "test-secret", options.Email.Domain),
            subject: $"RE: [{ticket.Reference}] Update",
            body: "Refund approved.",
            fromEmail: "agent@example.com");

        var result = await svc.ProcessAsync(message, audit);

        Assert.Equal(ProcessOutcome.CreatedNew, result.Outcome);
        Assert.NotEqual(ticket.Id, result.TicketId);
        Assert.Empty(db.Replies.Where(r => r.TicketId == ticket.Id));
        Assert.Empty(db.Replies.Where(r => r.AuthorId == "agent-1"));
    }

    [Fact]
    public async Task ProcessAsync_SecretConfigured_RequiresSignedReplyTo()
    {
        var (svc, db, _) = Create("test-secret");
        var ticket = await SeedTicket(db, guestEmail: "owner@example.com");
        var audit = SeedInboundAudit(db);
        var message = MakeMessage(
            inReplyTo: $"<ticket-{ticket.Id}@support.example.com>",
            toEmail: "support@support.example.com",
            subject: $"RE: [{ticket.Reference}] Question",
            body: "Unsigned follow-up.",
            fromEmail: "owner@example.com");

        var result = await svc.ProcessAsync(message, audit);

        Assert.Equal(ProcessOutcome.CreatedNew, result.Outcome);
        Assert.NotEqual(ticket.Id, result.TicketId);
        Assert.Empty(db.Replies.Where(r => r.TicketId == ticket.Id));
    }

    [Fact]
    public async Task ProcessAsync_SignedReplyFromRequester_PostsAsRequesterAndReopens()
    {
        var users = new FakeUserDirectory(new UserDirectoryEntry("user-7", "Owner", "owner@example.com"));
        var (svc, db, options) = Create("test-secret", users);
        var ticket = await SeedTicket(db, guestEmail: null, status: TicketStatus.Resolved, requesterId: "user-7");
        var audit = SeedInboundAudit(db);
        var message = MakeMessage(
            inReplyTo: $"<ticket-{ticket.Id}@support.example.com>",
            toEmail: MessageIdUtil.BuildReplyTo(ticket.Id, "test-secret", options.Email.Domain),
            subject: "RE: Question",
            body: "Still broken.",
            fromEmail: "Owner@Example.com");

        var result = await svc.ProcessAsync(message, audit);

        Assert.Equal(ProcessOutcome.RepliedToExisting, result.Outcome);
        Assert.Equal(ticket.Id, result.TicketId);
        var reply = db.Replies.Single(r => r.Id == result.ReplyId);
        Assert.Equal("user-7", reply.AuthorId);
        Assert.Equal(TicketStatus.Reopened, db.Tickets.Single(t => t.Id == ticket.Id).Status);
    }

    [Fact]
    public async Task ProcessAsync_ReplyFromContactEmail_IsAcceptedAsGuestReply()
    {
        var (svc, db, _) = Create();
        var contact = new Contact { Email = "guest@example.com" };
        db.Contacts.Add(contact);
        await db.SaveChangesAsync();
        var ticket = await SeedTicket(db, guestEmail: null);
        ticket.ContactId = contact.Id;
        await db.SaveChangesAsync();
        var audit = SeedInboundAudit(db);
        var message = MakeMessage(
            inReplyTo: $"<ticket-{ticket.Id}@support.example.com>",
            fromEmail: "GUEST@example.com");

        var result = await svc.ProcessAsync(message, audit);

        Assert.Equal(ProcessOutcome.RepliedToExisting, result.Outcome);
        Assert.Equal(ticket.Id, result.TicketId);
        Assert.Null(db.Replies.Single(r => r.Id == result.ReplyId).AuthorId);
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
