using Escalated.Services;
using Xunit;

namespace Escalated.Tests.Services;

public class AdvancedReportingServiceTests
{
    [Fact]
    public void CalculatePercentiles_ReturnsCorrectValues()
    {
        var values = new List<double> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        var result = AdvancedReportingService.CalculatePercentiles(values);
        Assert.Equal(5.5, result["p50"]);
        Assert.True(result.ContainsKey("p75"));
        Assert.True(result.ContainsKey("p90"));
        Assert.True(result.ContainsKey("p95"));
        Assert.True(result.ContainsKey("p99"));
    }

    [Fact]
    public void CalculatePercentiles_EmptyList_ReturnsEmpty()
    {
        var result = AdvancedReportingService.CalculatePercentiles(new List<double>());
        Assert.Empty(result);
    }

    [Fact]
    public void CompositeScore_ReturnsPositiveValue()
    {
        var score = AdvancedReportingService.CompositeScore(80, 2.0, 24.0, 4.5);
        Assert.True(score > 0);
    }

    [Fact]
    public void DateSeries_ReturnsCorrectCount()
    {
        var from = new DateTime(2024, 1, 1);
        var to = new DateTime(2024, 1, 10);
        var dates = AdvancedReportingService.DateSeries(from, to);
        Assert.Equal(10, dates.Count);
    }

    [Fact]
    public void DateSeries_CapsAt90Days()
    {
        var from = new DateTime(2024, 1, 1);
        var to = new DateTime(2024, 12, 31);
        var dates = AdvancedReportingService.DateSeries(from, to);
        Assert.Equal(90, dates.Count);
    }

    [Fact]
    public void CalculateChanges_ReturnsCorrectPercentages()
    {
        var current = new Dictionary<string, double> { { "total_created", 100 }, { "total_resolved", 80 }, { "resolution_rate", 80 } };
        var previous = new Dictionary<string, double> { { "total_created", 50 }, { "total_resolved", 40 }, { "resolution_rate", 80 } };
        var changes = AdvancedReportingService.CalculateChanges(current, previous);
        Assert.Equal(100, changes["total_created"]);
    }
}
