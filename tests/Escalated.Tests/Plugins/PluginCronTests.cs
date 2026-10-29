using Escalated.Plugins;
using Xunit;

namespace Escalated.Tests.Plugins;

public sealed class PluginCronTests
{
    [Theory]
    [InlineData("* * * * *", "2026-10-29T13:37:00Z", true)]
    [InlineData("37 13 * * *", "2026-10-29T13:37:45Z", true)]
    [InlineData("38 13 * * *", "2026-10-29T13:37:00Z", false)]
    [InlineData("*/15 * * * *", "2026-10-29T13:45:00Z", true)]
    [InlineData("*/15 * * * *", "2026-10-29T13:46:00Z", false)]
    [InlineData("0-10/5 9-17 * * *", "2026-10-29T09:10:00Z", true)]
    [InlineData("0-10/5 9-17 * * *", "2026-10-29T18:10:00Z", false)]
    [InlineData("1,2,3 * * * *", "2026-10-29T13:02:00Z", true)]
    // 2026-10-29 is a Thursday; 2026-11-01 a Sunday.
    [InlineData("0 9 * * thu", "2026-10-29T09:00:00Z", true)]
    [InlineData("0 9 * * MON-WED", "2026-10-29T09:00:00Z", false)]
    [InlineData("0 0 * * 7", "2026-11-01T00:00:00Z", true)]
    [InlineData("0 0 1 nov *", "2026-11-01T00:00:00Z", true)]
    [InlineData("0 0 13 * 4", "2026-10-29T00:00:00Z", true)]
    [InlineData("0 0 13 * 5", "2026-10-29T00:00:00Z", false)]
    [InlineData("0 0 29 * *", "2026-10-29T00:00:00Z", true)]
    [InlineData("0 9 * * *", "2026-10-29T05:00:00-04:00", true)]
    [InlineData("@hourly", "2026-10-29T13:00:00Z", true)]
    [InlineData("@daily", "2026-10-29T13:00:00Z", false)]
    [InlineData("@weekly", "2026-11-01T00:00:00Z", true)]
    public void IsDueInTheMinuteInUtc(string expression, string at, bool due) =>
        Assert.Equal(due, PluginCron.Due(expression, DateTimeOffset.Parse(at, System.Globalization.CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData("")]
    [InlineData("* * * *")]
    [InlineData("61 * * * *")]
    [InlineData("* * * * * *")]
    [InlineData("a b c d e")]
    [InlineData("*/0 * * * *")]
    [InlineData("5-1 * * * *")]
    public void RefusesWhatItCannotRead(string expression) =>
        Assert.Throws<FormatException>(() => PluginCron.Due(expression, DateTimeOffset.UtcNow));
}
