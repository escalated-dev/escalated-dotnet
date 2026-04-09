using Escalated.Services;
using Xunit;

namespace Escalated.Tests.Services;

public class MentionServiceTests
{
    private readonly MentionService _service = new();

    [Fact]
    public void ExtractMentions_SingleMention() =>
        Assert.Equal(new[] { "john" }, _service.ExtractMentions("Hello @john please review"));

    [Fact]
    public void ExtractMentions_MultipleMentions()
    {
        var result = _service.ExtractMentions("@alice and @bob please check");
        Assert.Contains("alice", result);
        Assert.Contains("bob", result);
    }

    [Fact]
    public void ExtractMentions_DottedUsername() =>
        Assert.Equal(new[] { "john.doe" }, _service.ExtractMentions("cc @john.doe"));

    [Fact]
    public void ExtractMentions_Deduplicates() =>
        Assert.Single(_service.ExtractMentions("@alice said @alice should review"));

    [Fact]
    public void ExtractMentions_Empty() =>
        Assert.Empty(_service.ExtractMentions(""));

    [Fact]
    public void ExtractMentions_NoMentions() =>
        Assert.Empty(_service.ExtractMentions("No mentions here"));

    [Fact]
    public void ExtractUsernameFromEmail_ReturnsUsername() =>
        Assert.Equal("john", _service.ExtractUsernameFromEmail("john@example.com"));
}
