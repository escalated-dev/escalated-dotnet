using System.Text;
using System.Text.Json;
using Escalated.Services.Email.Inbound;
using Xunit;

namespace Escalated.Tests.Services.Email.Inbound;

public class SESInboundParserTests
{
    private readonly SESInboundParser _parser = new();

    [Fact]
    public void Name_IsSes()
    {
        Assert.Equal("ses", _parser.Name);
    }

    [Fact]
    public async Task Parse_SubscriptionConfirmation_ThrowsWithSubscribeUrl()
    {
        var envelope = new
        {
            Type = "SubscriptionConfirmation",
            TopicArn = "arn:aws:sns:us-east-1:123:escalated-inbound",
            SubscribeURL = "https://sns.us-east-1.amazonaws.com/?Action=ConfirmSubscription&Token=x",
            Token = "abc",
        };

        var ex = await Assert.ThrowsAsync<SESSubscriptionConfirmationException>(
            () => _parser.ParseAsync(envelope));

        Assert.Equal("arn:aws:sns:us-east-1:123:escalated-inbound", ex.TopicArn);
        Assert.Contains("ConfirmSubscription", ex.SubscribeUrl);
        Assert.Equal("abc", ex.Token);
    }

    [Fact]
    public async Task Parse_Notification_ExtractsThreadingMetadata()
    {
        var sesMessage = new
        {
            notificationType = "Received",
            mail = new
            {
                source = "alice@example.com",
                destination = new[] { "support@example.com" },
                headers = new object[]
                {
                    new { name = "From", value = "Alice <alice@example.com>" },
                    new { name = "To", value = "support@example.com" },
                    new { name = "Subject", value = "[ESC-42] Re: Help" },
                    new { name = "Message-ID", value = "<external-xyz@mail.alice.com>" },
                    new { name = "In-Reply-To", value = "<ticket-42@support.example.com>" },
                    new { name = "References", value = "<ticket-42@support.example.com> <prev@mail.com>" },
                },
                commonHeaders = new
                {
                    from = new[] { "Alice <alice@example.com>" },
                    to = new[] { "support@example.com" },
                    subject = "[ESC-42] Re: Help",
                },
            },
        };

        var envelope = new
        {
            Type = "Notification",
            Message = JsonSerializer.Serialize(sesMessage),
        };

        var msg = await _parser.ParseAsync(envelope);

        Assert.Equal("alice@example.com", msg.FromEmail);
        Assert.Equal("Alice", msg.FromName);
        Assert.Equal("support@example.com", msg.ToEmail);
        Assert.Equal("[ESC-42] Re: Help", msg.Subject);
        Assert.Equal("<external-xyz@mail.alice.com>", msg.MessageId);
        Assert.Equal("<ticket-42@support.example.com>", msg.InReplyTo);
        Assert.Contains("ticket-42@support.example.com", msg.References);
        Assert.Equal("Alice <alice@example.com>", msg.Headers["From"]);
    }

    [Fact]
    public async Task Parse_Notification_DecodesPlainTextBody()
    {
        var mime = "From: alice@example.com\r\n" +
                   "To: support@example.com\r\n" +
                   "Subject: Hi\r\n" +
                   "Content-Type: text/plain; charset=\"utf-8\"\r\n" +
                   "\r\n" +
                   "This is the plain text body.";
        var contentB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(mime));

        var envelope = new
        {
            Type = "Notification",
            Message = JsonSerializer.Serialize(new
            {
                mail = new
                {
                    commonHeaders = new
                    {
                        from = new[] { "alice@example.com" },
                        to = new[] { "support@example.com" },
                        subject = "Hi",
                    },
                },
                content = contentB64,
            }),
        };

        var msg = await _parser.ParseAsync(envelope);

        Assert.Equal("This is the plain text body.", msg.BodyText);
    }

    [Fact]
    public async Task Parse_Notification_DecodesMultipartBody()
    {
        const string boundary = "boundary-abc";
        var mime = "From: alice@example.com\r\n" +
                   "To: support@example.com\r\n" +
                   "Subject: Hi\r\n" +
                   $"Content-Type: multipart/alternative; boundary=\"{boundary}\"\r\n" +
                   "\r\n" +
                   $"--{boundary}\r\n" +
                   "Content-Type: text/plain; charset=\"utf-8\"\r\n" +
                   "\r\n" +
                   "Plain body\r\n" +
                   $"--{boundary}\r\n" +
                   "Content-Type: text/html; charset=\"utf-8\"\r\n" +
                   "\r\n" +
                   "<p>HTML body</p>\r\n" +
                   $"--{boundary}--\r\n";
        var contentB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(mime));

        var envelope = new
        {
            Type = "Notification",
            Message = JsonSerializer.Serialize(new
            {
                mail = new
                {
                    commonHeaders = new
                    {
                        from = new[] { "alice@example.com" },
                        to = new[] { "support@example.com" },
                        subject = "Hi",
                    },
                },
                content = contentB64,
            }),
        };

        var msg = await _parser.ParseAsync(envelope);

        Assert.StartsWith("Plain body", msg.BodyText);
        Assert.StartsWith("<p>", msg.BodyHtml);
    }

    [Fact]
    public async Task Parse_Notification_MissingContent_LeavesBodyEmpty()
    {
        var envelope = new
        {
            Type = "Notification",
            Message = JsonSerializer.Serialize(new
            {
                mail = new
                {
                    commonHeaders = new
                    {
                        from = new[] { "alice@example.com" },
                        to = new[] { "support@example.com" },
                        subject = "Hi",
                    },
                },
            }),
        };

        var msg = await _parser.ParseAsync(envelope);

        Assert.Null(msg.BodyText);
        Assert.Null(msg.BodyHtml);
        Assert.Equal("alice@example.com", msg.FromEmail);
    }

    [Fact]
    public async Task Parse_UnknownType_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _parser.ParseAsync(new { Type = "UnknownEnvelopeType" }));
    }

    [Fact]
    public async Task Parse_MissingMessage_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _parser.ParseAsync(new { Type = "Notification" }));
    }

    [Fact]
    public async Task Parse_MalformedMessage_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _parser.ParseAsync(new { Type = "Notification", Message = "not json" }));
    }

    [Fact]
    public async Task Parse_FallsBackToHeadersArrayForThreadingFields()
    {
        // commonHeaders only has from/to/subject (no messageId /
        // inReplyTo / references) — fall back to the raw headers array.
        var envelope = new
        {
            Type = "Notification",
            Message = JsonSerializer.Serialize(new
            {
                mail = new
                {
                    headers = new object[]
                    {
                        new { name = "Message-ID", value = "<fallback@mail.com>" },
                        new { name = "In-Reply-To", value = "<ticket-99@support.example.com>" },
                    },
                    commonHeaders = new
                    {
                        from = new[] { "alice@example.com" },
                        to = new[] { "support@example.com" },
                        subject = "Fallback",
                    },
                },
            }),
        };

        var msg = await _parser.ParseAsync(envelope);

        Assert.Equal("<fallback@mail.com>", msg.MessageId);
        Assert.Equal("<ticket-99@support.example.com>", msg.InReplyTo);
    }
}
