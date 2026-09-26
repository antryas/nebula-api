using Nebula.Application.Common;

namespace Nebula.UnitTests.Common;

public sealed class WriteGateTests
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(200);

    [Fact]
    public async Task Holders_are_mutually_exclusive()
    {
        var gate = new WriteGate();
        var release = new TaskCompletionSource();
        var ct = TestContext.Current.CancellationToken;

        var first = gate.RunAsync(() => release.Task, ct);
        var secondEntered = false;
        var second = gate.RunAsync(() =>
        {
            secondEntered = true;
            return Task.CompletedTask;
        }, ct);

        await Task.Delay(Short, ct);
        Assert.False(secondEntered);

        release.SetResult();
        await Task.WhenAll(first, second);
        Assert.True(secondEntered);
    }

    [Fact]
    public async Task Is_reentrant_within_one_flow()
    {
        var gate = new WriteGate();
        var ct = TestContext.Current.CancellationToken;

        var result = await gate.RunAsync(() => gate.RunAsync(() => Task.FromResult(42), ct), ct).WaitAsync(Short * 10, ct);

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task Work_that_outlives_the_hold_does_not_bypass_the_gate()
    {
        var gate = new WriteGate();
        var ct = TestContext.Current.CancellationToken;
        var startLeaked = new TaskCompletionSource();
        Task? leaked = null;
        var leakedEntered = false;

        // Started inside the gate, so it inherits the holder's flow, but it only asks for the gate after the hold ended.
        await gate.RunAsync(() =>
        {
            leaked = Task.Run(async () =>
            {
                await startLeaked.Task;
                await gate.RunAsync(() =>
                {
                    leakedEntered = true;
                    return Task.CompletedTask;
                }, ct);
            }, ct);
            return Task.CompletedTask;
        }, ct);

        var release = new TaskCompletionSource();
        var other = gate.RunAsync(() => release.Task, ct);
        startLeaked.SetResult();
        await Task.Delay(Short, ct);
        Assert.False(leakedEntered);

        release.SetResult();
        await other;
        await leaked!;
        Assert.True(leakedEntered);
    }

    [Fact]
    public async Task Is_released_when_the_action_throws()
    {
        var gate = new WriteGate();
        var ct = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<InvalidOperationException>(() => gate.RunAsync(() => throw new InvalidOperationException(), ct));

        Assert.Equal(1, await gate.RunAsync(() => Task.FromResult(1), ct).WaitAsync(Short * 10, ct));
    }
}
