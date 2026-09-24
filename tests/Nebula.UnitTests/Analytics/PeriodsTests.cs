using Nebula.Application.Analytics;
using Nebula.Application.Common;

namespace Nebula.UnitTests.Analytics;

public sealed class PeriodsTests
{
    private static readonly DateTime Now = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(RevenueRange.D7, 7)]
    [InlineData(RevenueRange.D30, 30)]
    [InlineData(RevenueRange.D90, 90)]
    [InlineData(RevenueRange.M12, 12)]
    public void Bucket_count_matches_the_mock(RevenueRange range, int buckets)
    {
        var period = Periods.For(range, Now);

        Assert.Equal(buckets + 1, period.Edges.Count);
        Assert.Equal(Now, period.Edges[^1]);
    }

    [Fact]
    public void Daily_period_starts_n_days_ago_and_previous_period_has_equal_length()
    {
        var period = Periods.For(RevenueRange.D7, Now);

        Assert.Equal(Now.AddDays(-7), period.Start);
        Assert.Equal(Now.AddDays(-14), period.PreviousStart);
        Assert.Equal(Now.AddDays(-6), period.Edges[1]);
    }

    [Fact]
    public void Monthly_period_uses_calendar_months_and_24_month_previous_start()
    {
        var period = Periods.For(RevenueRange.M12, Now);

        Assert.Equal(new DateTime(2025, 9, 24, 12, 0, 0, DateTimeKind.Utc), period.Start);
        Assert.Equal(new DateTime(2025, 10, 24, 12, 0, 0, DateTimeKind.Utc), period.Edges[1]);
        Assert.Equal(new DateTime(2024, 9, 24, 12, 0, 0, DateTimeKind.Utc), period.PreviousStart);
    }

    [Fact]
    public void AddMonths_overflows_like_javascript_setUTCMonth()
    {
        var march31 = new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal(new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc), Periods.AddMonths(march31, -1));
    }

    [Fact]
    public void Bucketize_drops_items_before_start_and_leaves_last_bucket_open_ended()
    {
        DateTime[] edges = [Now.AddDays(-2), Now.AddDays(-1), Now];
        DateTime[] items = [Now.AddDays(-3), Now.AddDays(-2), Now.AddHours(-1), Now.AddHours(5)];

        var buckets = Periods.Bucketize(items, x => x, edges);

        Assert.Equal(2, buckets.Count);
        Assert.Equal([Now.AddDays(-2)], buckets[0]);
        Assert.Equal([Now.AddHours(-1), Now.AddHours(5)], buckets[1]);
    }

    [Fact]
    public void SplitEvenly_returns_parts_plus_one_edges()
    {
        var edges = Periods.SplitEvenly(Now.AddDays(-30), Now, 12);

        Assert.Equal(13, edges.Count);
        Assert.Equal(Now.AddDays(-30).AddHours(60), edges[1]);
    }

    [Theory]
    [InlineData(RevenueRange.D30, "2026-09-24")]
    [InlineData(RevenueRange.M12, "2026-09-01")]
    public void Bucket_label_uses_bucket_end(RevenueRange range, string expected) =>
        Assert.Equal(expected, Periods.BucketLabel(range, Now));

    [Theory]
    [InlineData(null, RevenueRange.D30)]
    [InlineData("7d", RevenueRange.D7)]
    [InlineData("30d", RevenueRange.D30)]
    [InlineData("90d", RevenueRange.D90)]
    [InlineData("12m", RevenueRange.M12)]
    public void Parser_accepts_wire_values(string? raw, RevenueRange expected) =>
        Assert.Equal(expected, RevenueRangeParser.Parse(raw));

    [Theory]
    [InlineData("5y")]
    [InlineData("")]
    [InlineData("7D")]
    public void Parser_rejects_unknown_values(string raw)
    {
        var error = Assert.Throws<ValidationFailedException>(() => RevenueRangeParser.Parse(raw));

        Assert.Equal($"Unknown range \"{raw}\"", error.Message);
        Assert.NotNull(error.Details);
        Assert.Equal("Expected one of 7d, 30d, 90d, 12m", error.Details["range"]);
    }
}
