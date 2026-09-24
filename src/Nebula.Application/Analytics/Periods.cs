using System.Globalization;
using Nebula.Application.Common;

namespace Nebula.Application.Analytics;

/// <summary>Dashboard time range; wire values <c>7d</c>, <c>30d</c>, <c>90d</c>, <c>12m</c>.</summary>
public enum RevenueRange
{
    D7,
    D30,
    D90,
    M12,
}

public static class RevenueRangeParser
{
    private static readonly (string Wire, RevenueRange Range)[] Ranges =
    [
        ("7d", RevenueRange.D7),
        ("30d", RevenueRange.D30),
        ("90d", RevenueRange.D90),
        ("12m", RevenueRange.M12),
    ];

    /// <summary>Missing ⇒ 30d; anything but an exact wire value ⇒ 422 <c>validation</c>.</summary>
    public static RevenueRange Parse(string? raw)
    {
        if (raw is null)
        {
            return RevenueRange.D30;
        }

        foreach (var (wire, range) in Ranges)
        {
            if (string.Equals(raw, wire, StringComparison.Ordinal))
            {
                return range;
            }
        }

        throw new ValidationFailedException(
            $"Unknown range \"{raw}\"",
            new Dictionary<string, string>
            {
                ["range"] = $"Expected one of {string.Join(", ", Ranges.Select(r => r.Wire))}",
            });
    }
}

/// <summary>Rolling window ending at "now".</summary>
/// <param name="Edges">Bucket boundaries, oldest first; <c>Edges.Count - 1</c> buckets.</param>
/// <param name="PreviousStart">Start of the previous period of equal length.</param>
public sealed record Period(IReadOnlyList<DateTime> Edges, DateTime PreviousStart)
{
    public DateTime Start => Edges[0];

    public DateTime End => Edges[^1];
}

/// <summary>Time-window helpers ported from <c>mock-api/handlers/analytics.ts</c>. All times are UTC.</summary>
public static class Periods
{
    /// <summary>
    /// Windows end at <paramref name="now"/> so the last bucket is a full day/month. Daily ranges have one
    /// bucket per day, <c>12m</c> has 12 calendar-month buckets.
    /// </summary>
    public static Period For(RevenueRange range, DateTime now)
    {
        if (range == RevenueRange.M12)
        {
            var monthEdges = Enumerable.Range(0, 13).Select(i => AddMonths(now, i - 12)).ToArray();
            return new Period(monthEdges, AddMonths(now, -24));
        }

        var days = range switch
        {
            RevenueRange.D7 => 7,
            RevenueRange.D30 => 30,
            RevenueRange.D90 => 90,
            _ => throw new ArgumentOutOfRangeException(nameof(range), range, null),
        };
        var edges = Enumerable.Range(0, days + 1).Select(i => now.AddDays(i - days)).ToArray();
        return new Period(edges, now.AddDays(-2 * days));
    }

    /// <summary>Month arithmetic with JavaScript <c>setUTCMonth</c> overflow (Mar 31 − 1 month = Mar 3).</summary>
    public static DateTime AddMonths(DateTime value, int months)
    {
        var firstOfMonth = new DateTime(value.Year, value.Month, 1, 0, 0, 0, value.Kind).AddMonths(months);
        return firstOfMonth.AddDays(value.Day - 1).Add(value.TimeOfDay);
    }

    public static IReadOnlyList<DateTime> SplitEvenly(DateTime from, DateTime to, int parts)
    {
        var span = to.Ticks - from.Ticks;
        return [.. Enumerable.Range(0, parts + 1).Select(i => new DateTime(from.Ticks + (span * i / parts), from.Kind))];
    }

    /// <summary>
    /// Groups items into <c>edges.Count - 1</c> buckets. Items before the first edge are dropped; the last
    /// bucket has no upper bound so live orders created after "now" land in it.
    /// </summary>
    public static IReadOnlyList<List<T>> Bucketize<T>(IEnumerable<T> items, Func<T, DateTime> at, IReadOnlyList<DateTime> edges)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(at);
        ArgumentNullException.ThrowIfNull(edges);

        var buckets = Enumerable.Range(0, edges.Count - 1).Select(_ => new List<T>()).ToArray();
        foreach (var item in items)
        {
            var time = at(item);
            if (time < edges[0])
            {
                continue;
            }

            var i = buckets.Length - 1;
            while (i > 0 && time < edges[i])
            {
                i--;
            }

            buckets[i].Add(item);
        }

        return buckets;
    }

    /// <summary>Daily buckets are labelled by the day they end on, monthly ones by the 1st of that month.</summary>
    public static string BucketLabel(RevenueRange range, DateTime bucketEnd) =>
        bucketEnd.ToString(range == RevenueRange.M12 ? "yyyy-MM-01" : "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
