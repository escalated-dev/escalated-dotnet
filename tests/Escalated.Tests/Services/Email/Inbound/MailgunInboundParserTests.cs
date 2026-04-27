using Escalated.Services.Email.Inbound;
using Xunit;

namespace Escalated.Tests.Services.Email.Inbound;

/// <summary>
/// Tests for <see cref="MailgunInboundParser"/>. Uses Mailgun's
/// form-data webhook shape (lifted from Mailgun's public docs).
/// </summary>
public class MailgunInboundParserTests
{
    private static IDictionary<string, string> SampleFormData() => new Dictionary<string, string>
    {
        // Mailgun canonical field names.
        ["sender"] = "customer@example.com",
        ["from"] = "Customer <customer@example.com>",
        ["recipient"] = "support+abc@support.example.com",
        ["To"] = "support+abc@support.example.com",
        ["subject"] = "[ESC-00042] Help",
        ["body-plain"] = "Plain body",
        ["body-html"] = "<p>HTML body</p>",
        ["Message-Id"] = "<mailgun-incoming@mail.client>",
        ["In-Reply-To"] = "<ticket-42@support.example.com>",
        ["References"] = "<ticket-42@support.example.com>",
        ["attachments"] = """
        [
          {"name": "report.pdf", "content-type": "application/pdf", "size": 5120, "url": "https://mailgun.example/att/abc"}
        ]
        """,
    };

    [Fact]
    public async Task ParseAsync_ExtractsCoreFields()
    {
        var parser = new MailgunInboundParser();
        var message = await parser.ParseAsync(SampleFormData());

        Assert.Equal("customer@example.com", message.FromEmail);
        Assert.Equal("Customer", message.FromName);
        Assert.Equal("support+abc@support.example.com", message.ToEmail);
        Assert.Equal("[ESC-00042] Help", message.Subject);
        Assert.Equal("Plain body", message.BodyText);
        Assert.Equal("<p>HTML body</p>", message.BodyHtml);
    }

    [Fact]
    public async Task ParseAsync_ExtractsThreadingHeaders()
    {
        var parser = new MailgunInboundParser();
        var message = await parser.ParseAsync(SampleFormData());

        Assert.Equal("<ticket-42@support.example.com>", message.InReplyTo);
        Assert.Equal("<ticket-42@support.example.com>", message.References);
    }

    [Fact]
    public async Task ParseAsync_ParsesProviderHostedAttachments()
    {
        var parser = new MailgunInboundParser();
        var message = await parser.ParseAsync(SampleFormData());

        Assert.Single(message.Attachments);
        var attachment = message.Attachments[0];
        Assert.Equal("report.pdf", attachment.Name);
        Assert.Equal("application/pdf", attachment.ContentType);
        Assert.Equal(5120, attachment.SizeBytes);
        Assert.Equal("https://mailgun.example/att/abc", attachment.DownloadUrl);
        // Mailgun hosts content — no inline bytes.
        Assert.Null(attachment.Content);
    }

    [Fact]
    public async Task ParseAsync_HandlesMalformedAttachmentsJson()
    {
        var parser = new MailgunInboundParser();
        var data = new Dictionary<string, string>(SampleFormData())
        {
            ["attachments"] = "not json",
        };

        var message = await parser.ParseAsync(data);

        Assert.Empty(message.Attachments);
    }

    [Fact]
    public async Task ParseAsync_FallsBackOnMissingSender()
    {
        var parser = new MailgunInboundParser();
        var data = new Dictionary<string, string>
        {
            ["from"] = "only-from@example.com",
            ["recipient"] = "support@example.com",
            ["subject"] = "hi",
        };

        var message = await parser.ParseAsync(data);

        // sender missing → falls back to from.
        Assert.Equal("only-from@example.com", message.FromEmail);
    }

    [Fact]
    public async Task ParseAsync_ExtractFromNameReturnsNullWithoutAngleBrackets()
    {
        var parser = new MailgunInboundParser();
        var data = new Dictionary<string, string>
        {
            ["sender"] = "bareemail@example.com",
            ["from"] = "bareemail@example.com",
            ["recipient"] = "support@example.com",
            ["subject"] = "hi",
        };

        var message = await parser.ParseAsync(data);

        Assert.Null(message.FromName);
    }

    [Fact]
    public void Name_IsMailgun()
    {
        Assert.Equal("mailgun", new MailgunInboundParser().Name);
    }
}
