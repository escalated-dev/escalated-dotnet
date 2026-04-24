using Escalated.Services.Email;
using Xunit;

namespace Escalated.Tests.Services.Email;

/// <summary>
/// Unit tests for <see cref="MessageIdUtil"/>. Mirrors the NestJS and
/// Spring reference test suites. Pure functions — no DI.
/// </summary>
public class MessageIdUtilTests
{
    private const string Domain = "support.example.com";
    private const string Secret = "test-secret-long-enough-for-hmac";

    [Fact]
    public void BuildMessageId_InitialTicket_UsesTicketForm()
    {
        var id = MessageIdUtil.BuildMessageId(42, null, Domain);
        Assert.Equal("<ticket-42@support.example.com>", id);
    }

    [Fact]
    public void BuildMessageId_ReplyForm_AppendsReplyId()
    {
        var id = MessageIdUtil.BuildMessageId(42, 7, Domain);
        Assert.Equal("<ticket-42-reply-7@support.example.com>", id);
    }

    [Fact]
    public void ParseTicketIdFromMessageId_RoundTripsBuiltId()
    {
        var initial = MessageIdUtil.BuildMessageId(42, null, Domain);
        var reply = MessageIdUtil.BuildMessageId(42, 7, Domain);

        Assert.Equal(42L, MessageIdUtil.ParseTicketIdFromMessageId(initial));
        Assert.Equal(42L, MessageIdUtil.ParseTicketIdFromMessageId(reply));
    }

    [Fact]
    public void ParseTicketIdFromMessageId_AcceptsValueWithoutBrackets()
    {
        Assert.Equal(99L, MessageIdUtil.ParseTicketIdFromMessageId("ticket-99@example.com"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<random@mail.com>")]
    [InlineData("ticket-abc@example.com")]
    public void ParseTicketIdFromMessageId_ReturnsNullForUnrelatedInput(string? input)
    {
        Assert.Null(MessageIdUtil.ParseTicketIdFromMessageId(input));
    }

    [Fact]
    public void BuildReplyTo_IsStableForSameInputs()
    {
        var first = MessageIdUtil.BuildReplyTo(42, Secret, Domain);
        var again = MessageIdUtil.BuildReplyTo(42, Secret, Domain);

        Assert.Equal(first, again);
        Assert.Matches(@"^reply\+42\.[a-f0-9]{8}@support\.example\.com$", first);
    }

    [Fact]
    public void BuildReplyTo_DifferentTicketsProduceDifferentSignatures()
    {
        var a = MessageIdUtil.BuildReplyTo(42, Secret, Domain);
        var b = MessageIdUtil.BuildReplyTo(43, Secret, Domain);
        Assert.NotEqual(a[..a.IndexOf('@')], b[..b.IndexOf('@')]);
    }

    [Fact]
    public void VerifyReplyTo_RoundTripsBuiltAddress()
    {
        var address = MessageIdUtil.BuildReplyTo(42, Secret, Domain);
        Assert.Equal(42L, MessageIdUtil.VerifyReplyTo(address, Secret));
    }

    [Fact]
    public void VerifyReplyTo_AcceptsLocalPartOnly()
    {
        var address = MessageIdUtil.BuildReplyTo(42, Secret, Domain);
        var local = address[..address.IndexOf('@')];
        Assert.Equal(42L, MessageIdUtil.VerifyReplyTo(local, Secret));
    }

    [Fact]
    public void VerifyReplyTo_RejectsTamperedSignature()
    {
        var address = MessageIdUtil.BuildReplyTo(42, Secret, Domain);
        var at = address.IndexOf('@');
        var local = address[..at];
        var last = local[^1];
        var tampered = local[..^1] + (last == '0' ? '1' : '0') + address[at..];

        Assert.Null(MessageIdUtil.VerifyReplyTo(tampered, Secret));
    }

    [Fact]
    public void VerifyReplyTo_RejectsWrongSecret()
    {
        var address = MessageIdUtil.BuildReplyTo(42, Secret, Domain);
        Assert.Null(MessageIdUtil.VerifyReplyTo(address, "different-secret"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("alice@example.com")]
    [InlineData("reply@example.com")]
    [InlineData("reply+abc.deadbeef@example.com")]
    public void VerifyReplyTo_RejectsMalformedInput(string? input)
    {
        Assert.Null(MessageIdUtil.VerifyReplyTo(input, Secret));
    }

    [Fact]
    public void VerifyReplyTo_IsCaseInsensitiveOnHex()
    {
        var address = MessageIdUtil.BuildReplyTo(42, Secret, Domain);
        Assert.Equal(42L, MessageIdUtil.VerifyReplyTo(address.ToUpperInvariant(), Secret));
    }
}
