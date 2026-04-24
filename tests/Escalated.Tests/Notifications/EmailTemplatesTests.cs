using Escalated.Configuration;
using Escalated.Models;
using Escalated.Notifications;
using Xunit;

namespace Escalated.Tests.Notifications;

/// <summary>
/// Tests for <see cref="EmailTemplates"/> — verifies the
/// <see cref="MessageIdUtil"/> wire-up produces the canonical
/// Message-ID / In-Reply-To / References headers plus a signed
/// Reply-To when <see cref="EmailOptions.InboundSecret"/> is set.
/// </summary>
public class EmailTemplatesTests
{
    private static EmailOptions WithSecret(string domain = "support.example.com") =>
        new() { Domain = domain, InboundSecret = "test-secret-for-hmac" };

    private static EmailOptions WithoutSecret(string domain = "support.example.com") =>
        new() { Domain = domain, InboundSecret = string.Empty };

    private static Ticket NewTicket(int id = 42) => new()
    {
        Id = id,
        Reference = "ESC-00042",
        Subject = "Help with login",
        Description = "Body",
    };

    [Fact]
    public void NewTicket_SetsCanonicalMessageIdAndSignedReplyTo()
    {
        var ticket = NewTicket();
        var msg = EmailTemplates.NewTicket(ticket, "support@example.com", WithSecret());

        Assert.Equal("<ticket-42@support.example.com>", msg.MessageId);
        Assert.Null(msg.InReplyTo);
        Assert.NotNull(msg.ReplyTo);
        Assert.Matches(@"^reply\+42\.[a-f0-9]{8}@support\.example\.com$", msg.ReplyTo!);
    }

    [Fact]
    public void NewTicket_WhenSecretBlank_OmitsReplyTo()
    {
        var ticket = NewTicket();
        var msg = EmailTemplates.NewTicket(ticket, "support@example.com", WithoutSecret());

        Assert.Equal("<ticket-42@support.example.com>", msg.MessageId);
        Assert.Null(msg.ReplyTo);
    }

    [Fact]
    public void TicketReply_BuildsReplyMessageIdAndThreadingHeaders()
    {
        var ticket = NewTicket();
        var reply = new Reply { Id = 7, Body = "Follow-up" };

        var msg = EmailTemplates.TicketReply(ticket, reply, "user@example.com", WithSecret());

        Assert.Equal("<ticket-42-reply-7@support.example.com>", msg.MessageId);
        Assert.Equal("<ticket-42@support.example.com>", msg.InReplyTo);
        Assert.Equal("<ticket-42@support.example.com>", msg.References);
        Assert.Matches(@"^reply\+42\.[a-f0-9]{8}@support\.example\.com$", msg.ReplyTo!);
    }

    [Fact]
    public void SlaBreachAlert_ThreadsOffTicketRootAndHasSignedReplyTo()
    {
        var ticket = NewTicket();

        var msg = EmailTemplates.SlaBreachAlert(ticket, "first_response", "oncall@example.com", WithSecret());

        Assert.Equal("<ticket-42@support.example.com>", msg.InReplyTo);
        Assert.Equal("<ticket-42@support.example.com>", msg.References);
        Assert.Matches(@"^reply\+42\.[a-f0-9]{8}@support\.example\.com$", msg.ReplyTo!);
    }

    [Fact]
    public void SlaBreachAlert_WhenSecretBlank_OmitsReplyTo()
    {
        var ticket = NewTicket();

        var msg = EmailTemplates.SlaBreachAlert(ticket, "resolution", "oncall@example.com", WithoutSecret());

        Assert.Null(msg.ReplyTo);
    }

    [Fact]
    public void EmailOptions_Defaults_DisableSigningAndUseLocalhost()
    {
        var options = new EscalatedOptions();
        Assert.Equal("localhost", options.Email.Domain);
        Assert.Equal(string.Empty, options.Email.InboundSecret);
    }
}
