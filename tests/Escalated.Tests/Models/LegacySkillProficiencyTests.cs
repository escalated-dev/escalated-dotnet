using Escalated.Models;
using Xunit;

namespace Escalated.Tests.Models;

public class LegacySkillProficiencyTests
{
    [Theory]
    [InlineData(null, 3)]
    [InlineData("", 3)]
    [InlineData("   ", 3)]
    [InlineData("beginner", 1)]
    [InlineData("Beginner", 1)]
    [InlineData("intermediate", 3)]
    [InlineData("expert", 5)]
    [InlineData("bogus", 3)]
    [InlineData("unknown-level", 3)]
    public void Parse_NormalizesHistoricalStrings(string? raw, int expected)
        => Assert.Equal(expected, LegacySkillProficiency.Parse(raw));
}
