using System.Text.Json;
using Escalated.Services.Email.Inbound;
using Xunit;

namespace Escalated.Tests.Services.Email.Inbound;

/// <summary>
/// Tests for <see cref="PostmarkInboundParser"/>. Uses Postmark's
/// actual webhook payload shape (lifted from their public docs).
/// </summary>
public class PostmarkInboundParserTests
{
    private static readonly string SamplePayload = """
    {
      "FromName": "Customer",
      "MessageID": "22c74902-a0c1-4511-804f2-341342852c90",
      "FromFull": { "Email": "customer@example.com", "Name": "Customer" },
      "To": "support+abc@support.example.com",
      "ToFull": [ { "Email": "support+abc@support.example.com", "Name": "" } ],
      "OriginalRecipient": "support+abc@support.example.com",
      "Subject": "[ESC-00042] Help",
      "Date": "Thu, 24 Apr 2026 08:00:00 +0000",
      "TextBody": "Plain body",
      "HtmlBody": "<p>HTML body</p>",
      "Headers": [
        { "Name": "Message-ID", "Value": "<abc@mail.client>" },
        { "Name": "In-Reply-To", "Value": "<ticket-42@support.example.com>" },
        { "Name": "References", "Value": "<ticket-42@support.example.com>" }
      ],
      "Attachments": [
        {
          "Name": "report.pdf",
          "Content": "aGVsbG8=",
          "ContentType": "application/pdf",
          "ContentLength": 5
        }
      ]
    }
    """;

    [Fact]
    public async Task ParseAsync_ExtractsCoreFields()
    {
        var parser = new PostmarkInboundParser();
        var payload = JsonSerializer.Deserialize<JsonElement>(SamplePayload);

        var message = await parser.ParseAsync(payload);

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
        var parser = new PostmarkInboundParser();
        var payload = JsonSerializer.Deserialize<JsonElement>(SamplePayload);

        var message = await parser.ParseAsync(payload);

        Assert.Equal("<ticket-42@support.example.com>", message.InReplyTo);
        Assert.Equal("<ticket-42@support.example.com>", message.References);
    }

    [Fact]
    public async Task ParseAsync_ExtractsAttachments()
    {
        var parser = new PostmarkInboundParser();
        var payload = JsonSerializer.Deserialize<JsonElement>(SamplePayload);

        var message = await parser.ParseAsync(payload);

        Assert.Single(message.Attachments);
        var attachment = message.Attachments[0];
        Assert.Equal("report.pdf", attachment.Name);
        Assert.Equal("application/pdf", attachment.ContentType);
        Assert.Equal(5, attachment.SizeBytes);
        Assert.NotNull(attachment.Content);
        Assert.Equal("hello", System.Text.Encoding.UTF8.GetString(attachment.Content!));
    }

    [Fact]
    public async Task ParseAsync_HandlesMissingOptionalFields()
    {
        var parser = new PostmarkInboundParser();
        var minimalPayload = """
        {
          "FromFull": { "Email": "a@b.com" },
          "ToFull": [ { "Email": "c@d.com" } ],
          "Subject": "minimal"
        }
        """;
        var payload = JsonSerializer.Deserialize<JsonElement>(minimalPayload);

        var message = await parser.ParseAsync(payload);

        Assert.Equal("a@b.com", message.FromEmail);
        Assert.Null(message.FromName);
        Assert.Equal("c@d.com", message.ToEmail);
        Assert.Equal("minimal", message.Subject);
        Assert.Null(message.BodyText);
        Assert.Null(message.InReplyTo);
        Assert.Empty(message.Attachments);
    }

    [Fact]
    public void Name_IsPostmark()
    {
        Assert.Equal("postmark", new PostmarkInboundParser().Name);
    }
}
