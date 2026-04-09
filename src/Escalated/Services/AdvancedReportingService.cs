using System;
using System.Collections.Generic;
using System.Linq;

namespace Escalated.Services;

public class AdvancedReportingService
{
    public static Dictionary<string, double> CalculatePercentiles(List<double> values)
    {
        if (values == null || values.Count == 0)
            return new Dictionary<string, double>();

        var sorted = values.OrderBy(v => v).ToList();
        return new Dictionary<string, double>
        {
            ["p50"] = PercentileValue(sorted, 50),
            ["p75"] = PercentileValue(sorted, 75),
            ["p90"] = PercentileValue(sorted, 90),
            ["p95"] = PercentileValue(sorted, 95),
            ["p99"] = PercentileValue(sorted, 99),
        };
    }

    public static double PercentileValue(List<double> sorted, double p)
    {
        if (sorted.Count == 1) return Math.Round(sorted[0], 2);
        var k = (p / 100) * (sorted.Count - 1);
        var f = (int)Math.Floor(k);
        var c = (int)Math.Ceiling(k);
        if (f == c) return Math.Round(sorted[f], 2);
        return Math.Round(sorted[f] + (k - f) * (sorted[c] - sorted[f]), 2);
    }

    public static object BuildDistribution(List<double> values, string unit)
    {
        if (values == null || values.Count == 0)
            return new { buckets = Array.Empty<object>(), stats = new { } };

        var sorted = values.OrderBy(v => v).ToList();
        var max = sorted.Last();
        var bucketSize = Math.Max((int)Math.Ceiling(max / 10), 1);
        var buckets = new List<object>();
        for (var start = 0; start <= (int)Math.Ceiling(max); start += bucketSize)
        {
            var end = start + bucketSize;
            var count = sorted.Count(v => v >= start && v < end);
            if (count > 0) buckets.Add(new { range = $"{start}-{end}", count });
        }

        return new
        {
            buckets,
            stats = new
            {
                min = sorted.First(),
                max = sorted.Last(),
                avg = Math.Round(sorted.Average(), 2),
                median = PercentileValue(sorted, 50),
                count = sorted.Count,
                unit
            },
            percentiles = CalculatePercentiles(sorted)
        };
    }

    public static double CompositeScore(double resolutionRate, double? avgFrt, double? avgResolution, double? avgCsat)
    {
        double score = 0, weights = 0;
        score += (resolutionRate / 100) * 30; weights += 30;
        if (avgFrt.HasValue && avgFrt > 0) { score += Math.Max(1 - avgFrt.Value / 24, 0) * 25; weights += 25; }
        if (avgResolution.HasValue && avgResolution > 0) { score += Math.Max(1 - avgResolution.Value / 72, 0) * 25; weights += 25; }
        if (avgCsat.HasValue) { score += (avgCsat.Value / 5) * 20; weights += 20; }
        return weights > 0 ? Math.Round((score / weights) * 100, 1) : 0;
    }

    public static List<DateTime> DateSeries(DateTime from, DateTime to)
    {
        var days = Math.Min(Math.Max((to - from).Days + 1, 1), 90);
        return Enumerable.Range(0, days).Select(i => from.AddDays(i)).ToList();
    }

    public static Dictionary<string, double> CalculateChanges(Dictionary<string, double> current, Dictionary<string, double> previous)
    {
        var changes = new Dictionary<string, double>();
        foreach (var key in new[] { "total_created", "total_resolved", "resolution_rate" })
        {
            var cur = current.GetValueOrDefault(key, 0);
            var prev = previous.GetValueOrDefault(key, 0);
            changes[key] = prev == 0 ? (cur > 0 ? 100 : 0) : Math.Round((cur - prev) / prev * 100, 1);
        }
        return changes;
    }
}
