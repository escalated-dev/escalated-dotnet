using System.Text;
using System.Text.Json;
using Escalated.Services.Email.Inbound;
using Xunit;

namespace Escalated.Tests.Services.Email.Inbound;

/// <summary>
/// Parser-equivalence tests: the same logical email, expressed in
/// each provider's native webhook payload shape, should normalize to
/// the same <see cref="InboundMessage"/> metadata. Parser equivalence
/// at this layer guarantees a reply delivered via any provider
/// routes to the same ticket via the same threading chain.
///
/// <para>Mirrors escalated-go#37. Adding a fourth provider in the
/// future can reuse the same <c>LogicalEmail</c> → provider-payload
/// builders and get contract validation for free.</para>
/// </summary>
public class ParserEquivalenceTests
{
    private record LogicalEmail(
        string FromEmail,
        string FromName,
        string ToEmail,
        string Subject,
        string BodyText,
        string MessageId,
        string InReplyTo,
        string References);

    private static readonly LogicalEmail Sample = new(
        FromEmail: "alice@example.com",
        FromName: "Alice",
        ToEmail: "support@example.com",
        Subject: "Re: Help with invoice",
        BodyText: "Thanks for the quick response.",
        MessageId: "<external-reply-xyz@mail.alice.com>",
        InReplyTo: "<ticket-42@support.example.com>",
        References: "<ticket-42@support.example.com>");

    private static object BuildPostmarkPayload(LogicalEmail e) => new
    {
        FromFull = new { Email = e.FromEmail, Name = e.FromName },
        To = e.ToEmail,
        Subject = e.Subject,
        TextBody = e.BodyText,
        Headers = new object[]
        {
            new { Name = "Message-ID", Value = e.MessageId },
            new { Name = "In-Reply-To", Value = e.InReplyTo },
            new { Name = "References", Value = e.References },
        },
    };

    private static object BuildMailgunPayload(LogicalEmail e) => new Dictionary<string, object>
    {
        ["sender"] = e.FromEmail,
        ["from"] = $"{e.FromName} <{e.FromEmail}>",
        ["recipient"] = e.ToEmail,
        ["subject"] = e.Subject,
        ["body-plain"] = e.BodyText,
        ["Message-Id"] = e.MessageId,
        ["In-Reply-To"] = e.InReplyTo,
        ["References"] = e.References,
    };

    private static object BuildSESPayload(LogicalEmail e)
    {
        // Include full raw MIME as base64 so body extraction is
        // exercised. The equivalence check on metadata-only could run
        // without this, but keeping the payload close to a real SES
        // delivery catches more drift.
        var mime = $"From: {e.FromName} <{e.FromEmail}>\r\n"
            + $"To: {e.ToEmail}\r\n"
            + $"Subject: {e.Subject}\r\n"
            + $"Message-ID: {e.MessageId}\r\n"
            + $"In-Reply-To: {e.InReplyTo}\r\n"
            + $"References: {e.References}\r\n"
            + "Content-Type: text/plain; charset=\"utf-8\"\r\n"
            + "\r\n"
            + e.BodyText;

        var sesMessage = new
        {
            notificationType = "Received",
            mail = new
            {
                source = e.FromEmail,
                destination = new[] { e.ToEmail },
                headers = new object[]
                {
                    new { name = "From", value = $"{e.FromName} <{e.FromEmail}>" },
                    new { name = "To", value = e.ToEmail },
                    new { name = "Subject", value = e.Subject },
                    new { name = "Message-ID", value = e.MessageId },
                    new { name = "In-Reply-To", value = e.InReplyTo },
                    new { name = "References", value = e.References },
                },
                commonHeaders = new
                {
                    from = new[] { $"{e.FromName} <{e.FromEmail}>" },
                    to = new[] { e.ToEmail },
                    subject = e.Subject,
                },
            },
            content = Convert.ToBase64String(Encoding.UTF8.GetBytes(mime)),
        };
        return new
        {
            Type = "Notification",
            Message = JsonSerializer.Serialize(sesMessage),
        };
    }

    [Fact]
    public async Task NormalizesToSameMessage()
    {
        var postmark = await new PostmarkInboundParser().ParseAsync(BuildPostmarkPayload(Sample));
        var mailgun = await new MailgunInboundParser().ParseAsync(BuildMailgunPayload(Sample));
        var ses = await new SESInboundParser().ParseAsync(BuildSESPayload(Sample));

        foreach (var (name, msg) in new[]
        {
            ("postmark", postmark),
            ("mailgun", mailgun),
            ("ses", ses),
        })
        {
            Assert.Equal(Sample.FromEmail, msg.FromEmail);
            Assert.Equal(Sample.ToEmail, msg.ToEmail);
            Assert.Equal(Sample.Subject, msg.Subject);
            Assert.Equal(Sample.InReplyTo, msg.InReplyTo);
            Assert.Equal(Sample.References, msg.References);
            // Verify via name so a failure message points at the
            // offending parser; xUnit appends the assertion path.
            Assert.True(msg.FromEmail == Sample.FromEmail, $"{name}: FromEmail mismatch");
        }
    }

    [Fact]
    public async Task BodyExtractionMatches()
    {
        var postmark = await new PostmarkInboundParser().ParseAsync(BuildPostmarkPayload(Sample));
        var mailgun = await new MailgunInboundParser().ParseAsync(BuildMailgunPayload(Sample));
        var ses = await new SESInboundParser().ParseAsync(BuildSESPayload(Sample));

        Assert.Equal(Sample.BodyText, postmark.BodyText);
        Assert.Equal(Sample.BodyText, mailgun.BodyText);
        Assert.Equal(Sample.BodyText, ses.BodyText);
    }
}
