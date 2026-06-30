using Escalated.Services;
using Xunit;

namespace Escalated.Tests.Services;

public class FollowerRecipientsTests
{
    [Fact]
    public void ExcludesActorAndDeduplicates()
    {
        Assert.Equal(new[] { "7", "3" }, FollowerRecipients.Resolve(new[] { "7", "2", "7", "3" }, "2"));
    }

    [Fact]
    public void KeepsAllDeduplicatedWhenNoActorExcluded()
    {
        Assert.Equal(new[] { "7", "3" }, FollowerRecipients.Resolve(new[] { "7", "3", "7" }, null));
    }
}
