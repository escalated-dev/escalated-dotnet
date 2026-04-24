using Escalated.Configuration;
using Escalated.Enums;
using Escalated.Models;
using Escalated.Services.Email;
using Escalated.Services.Email.Inbound;
using Xunit;

namespace Escalated.Tests.Services.Email.Inbound;

/// <summary>
/// Unit tests for <see cref="InboundEmailRouter"/> — verifies the
/// 5-priority resolution order + forged-signature rejection.
/// Uses an in-memory EF Core DB + real EscalatedOptions so the
/// MessageIdUtil wire-up is exercised end-to-end.
/// </summary>
public class InboundEmailRouterTests
{
    private static (InboundEmailRouter router, Data.EscalatedDbContext db, EscalatedOptions options)
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
        return (new InboundEmailRouter(db, options), db, options);
    }

    private static async Task<Ticket> SeedTicket(Data.EscalatedDbContext db, string reference = "ESC-00042")
    {
        var ticket = new Ticket
        {
            Reference = reference,
            Subject = "Test",
            Status = TicketStatus.Open,
            Priority = TicketPriority.Medium,
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();
        return ticket;
    }

    private static InboundMessage MakeMessage(
        string? inReplyTo = null,
        string? references = null,
        string? toEmail = "support@example.com",
        string subject = "hello")
        => new()
        {
            FromEmail = "customer@example.com",
            ToEmail = toEmail ?? string.Empty,
            Subject = subject,
            BodyText = "body",
            InReplyTo = inReplyTo,
            References = references,
        };

    [Fact]
    public async Task ResolveTicketAsync_MatchesInReplyToCanonicalMessageId()
    {
        var (router, db, _) = Create();
        var ticket = await SeedTicket(db);
        var message = MakeMessage(
            inReplyTo: $"<ticket-{ticket.Id}@support.example.com>");

        var found = await router.ResolveTicketAsync(message);

        Assert.NotNull(found);
        Assert.Equal(ticket.Id, found.Id);
    }

    [Fact]
    public async Task ResolveTicketAsync_MatchesReferencesHeaderCanonicalMessageId()
    {
        var (router, db, _) = Create();
        var ticket = await SeedTicket(db);
        var message = MakeMessage(
            references: $"<unrelated@mail.com> <ticket-{ticket.Id}@support.example.com>");

        var found = await router.ResolveTicketAsync(message);

        Assert.NotNull(found);
        Assert.Equal(ticket.Id, found.Id);
    }

    [Fact]
    public async Task ResolveTicketAsync_VerifiesSignedReplyToWhenSecretConfigured()
    {
        var (router, db, options) = Create("test-secret");
        var ticket = await SeedTicket(db);
        var to = MessageIdUtil.BuildReplyTo(ticket.Id, options.Email.InboundSecret, options.Email.Domain);
        var message = MakeMessage(toEmail: to);

        var found = await router.ResolveTicketAsync(message);

        Assert.NotNull(found);
        Assert.Equal(ticket.Id, found.Id);
    }

    [Fact]
    public async Task ResolveTicketAsync_RejectsForgedReplyToSignature()
    {
        var (router, db, _) = Create("real-secret");
        var ticket = await SeedTicket(db);
        // Signed with a DIFFERENT secret.
        var forged = MessageIdUtil.BuildReplyTo(ticket.Id, "wrong-secret", "support.example.com");
        var message = MakeMessage(toEmail: forged);

        var found = await router.ResolveTicketAsync(message);

        Assert.Null(found);
    }

    [Fact]
    public async Task ResolveTicketAsync_IgnoresSignedReplyToWhenSecretBlank()
    {
        var (router, db, _) = Create();
        var ticket = await SeedTicket(db);
        // Even a valid address signed with SOME secret must be
        // ignored when the host hasn't configured one.
        var to = MessageIdUtil.BuildReplyTo(ticket.Id, "test-secret", "support.example.com");
        var message = MakeMessage(toEmail: to);

        var found = await router.ResolveTicketAsync(message);

        Assert.Null(found);
    }

    [Fact]
    public async Task ResolveTicketAsync_MatchesSubjectReferenceTag()
    {
        var (router, db, _) = Create();
        var ticket = await SeedTicket(db, reference: "ESC-00099");
        var message = MakeMessage(subject: "RE: [ESC-00099] help");

        var found = await router.ResolveTicketAsync(message);

        Assert.NotNull(found);
        Assert.Equal(ticket.Id, found.Id);
    }

    [Fact]
    public async Task ResolveTicketAsync_FallsBackToInboundEmailLookup()
    {
        var (router, db, _) = Create();
        var ticket = await SeedTicket(db);
        // Legacy Message-ID shape — not parseable by the canonical
        // util but stored in InboundEmail from a prior outbound.
        var legacyMsgId = "<pre-migration-{ticket.Id}@old.escalated>";
        db.Set<InboundEmail>().Add(new InboundEmail
        {
            MessageId = legacyMsgId,
            FromEmail = "support@example.com",
            Status = "processed",
            TicketId = ticket.Id,
        });
        await db.SaveChangesAsync();

        var message = MakeMessage(inReplyTo: legacyMsgId);

        var found = await router.ResolveTicketAsync(message);

        Assert.NotNull(found);
        Assert.Equal(ticket.Id, found.Id);
    }

    [Fact]
    public async Task ResolveTicketAsync_ReturnsNullWhenNothingMatches()
    {
        var (router, db, _) = Create();
        await SeedTicket(db);
        var message = MakeMessage(subject: "Completely unrelated");

        var found = await router.ResolveTicketAsync(message);

        Assert.Null(found);
    }

    [Fact]
    public void CandidateHeaderMessageIds_InReplyToFirst_ThenReferences()
    {
        var message = MakeMessage(
            inReplyTo: "<primary@mail>",
            references: "<a@mail> <b@mail>");

        var ids = InboundEmailRouter.CandidateHeaderMessageIds(message).ToList();

        Assert.Equal(3, ids.Count);
        Assert.Equal("<primary@mail>", ids[0]);
        Assert.Equal("<a@mail>", ids[1]);
        Assert.Equal("<b@mail>", ids[2]);
    }

    [Fact]
    public void CandidateHeaderMessageIds_EmptyHeaders_YieldsNone()
    {
        var message = MakeMessage();

        var ids = InboundEmailRouter.CandidateHeaderMessageIds(message).ToList();

        Assert.Empty(ids);
    }
}
