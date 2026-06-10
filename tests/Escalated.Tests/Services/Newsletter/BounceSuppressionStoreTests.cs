using Escalated.Services.Newsletter;
using Xunit;

namespace Escalated.Tests.Services.Newsletter;

public class BounceSuppressionStoreTests
{
    [Fact]
    public async Task MarkBouncedAsync_IsBouncedAsync_RoundTripIsCaseInsensitive()
    {
        var db = TestHelpers.CreateInMemoryDb();
        var store = new BounceSuppressionStore(db);

        await store.MarkBouncedAsync("User@Example.COM");

        Assert.True(await store.IsBouncedAsync("user@example.com"));
        Assert.True(await store.IsBouncedAsync("USER@EXAMPLE.COM"));
        Assert.False(await store.IsBouncedAsync("other@example.com"));
    }

    [Fact]
    public async Task FilterSendableAsync_ExcludesSuppressedEmails()
    {
        var db = TestHelpers.CreateInMemoryDb();
        var store = new BounceSuppressionStore(db);

        await store.MarkBouncedAsync("blocked@example.com");

        var sendable = await store.FilterSendableAsync(
            new[] { "ok@example.com", "Blocked@Example.com", "also@example.com" });

        Assert.Equal(new[] { "ok@example.com", "also@example.com" }, sendable);
    }
}
