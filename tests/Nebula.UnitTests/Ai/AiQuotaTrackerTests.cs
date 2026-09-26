using Microsoft.Extensions.Options;
using Nebula.Application.Ai;
using Nebula.UnitTests.TestSupport;

namespace Nebula.UnitTests.Ai;

public sealed class AiQuotaTrackerTests
{
    private static (AiQuotaTracker Tracker, FakeClock Clock) Create(int perClient, int total)
    {
        var clock = new FakeClock(new DateTime(2026, 9, 24, 23, 59, 0, DateTimeKind.Utc));
        var options = Options.Create(new AiOptions { DailyRequestsPerClient = perClient, DailyRequestsTotal = total });
        return (new AiQuotaTracker(clock, options), clock);
    }

    [Fact]
    public void Fresh_client_has_the_full_per_client_allowance()
    {
        var (tracker, _) = Create(perClient: 3, total: 10);

        Assert.Equal(new AiQuota(3, 3), tracker.Peek("1.1.1.1"));
    }

    [Fact]
    public void Each_client_has_its_own_daily_allowance()
    {
        var (tracker, _) = Create(perClient: 2, total: 10);

        Assert.True(tracker.TryConsume("a", out var first));
        Assert.Equal(1, first.Remaining);
        Assert.True(tracker.TryConsume("a", out var second));
        Assert.Equal(0, second.Remaining);

        Assert.False(tracker.TryConsume("a", out var rejected));
        Assert.Equal(new AiQuota(0, 2), rejected);
        Assert.Equal(new AiQuota(2, 2), tracker.Peek("b"));
        Assert.True(tracker.TryConsume("b", out _));
    }

    [Fact]
    public void Global_allowance_caps_all_clients_together()
    {
        var (tracker, _) = Create(perClient: 5, total: 3);

        Assert.True(tracker.TryConsume("a", out _));
        Assert.True(tracker.TryConsume("b", out _));
        Assert.True(tracker.TryConsume("c", out var last));
        Assert.Equal(0, last.Remaining);

        Assert.False(tracker.TryConsume("d", out var rejected));
        Assert.Equal(new AiQuota(0, 5), rejected);
        Assert.Equal(0, tracker.Peek("a").Remaining);
    }

    [Fact]
    public void Remaining_is_the_smaller_of_client_and_global_allowance()
    {
        var (tracker, _) = Create(perClient: 3, total: 4);

        Assert.True(tracker.TryConsume("a", out _));
        Assert.Equal(new AiQuota(2, 3), tracker.Peek("a")); // client-bound: 3 - 1
        Assert.True(tracker.TryConsume("b", out _));
        Assert.True(tracker.TryConsume("b", out _));

        Assert.Equal(new AiQuota(1, 3), tracker.Peek("c")); // global-bound: 4 - 3
    }

    [Fact]
    public void Counters_reset_at_midnight_utc()
    {
        var (tracker, clock) = Create(perClient: 1, total: 1);
        Assert.True(tracker.TryConsume("a", out _));
        Assert.False(tracker.TryConsume("a", out _));

        clock.UtcNow = clock.UtcNow.AddMinutes(1);

        Assert.Equal(new AiQuota(1, 1), tracker.Peek("a"));
        Assert.True(tracker.TryConsume("a", out _));
    }

    [Fact]
    public void Peek_does_not_consume()
    {
        var (tracker, _) = Create(perClient: 1, total: 1);

        _ = tracker.Peek("a");
        _ = tracker.Peek("a");

        Assert.True(tracker.TryConsume("a", out _));
    }

    [Fact]
    public async Task Concurrent_requests_never_exceed_the_global_allowance()
    {
        var (tracker, _) = Create(perClient: 1000, total: 50);

        var results = await Task.WhenAll(Enumerable.Range(0, 200).Select(i =>
            Task.Run(() => tracker.TryConsume($"client-{i % 7}", out _), TestContext.Current.CancellationToken)));

        Assert.Equal(50, results.Count(granted => granted));
    }
}
