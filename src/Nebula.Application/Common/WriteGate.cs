namespace Nebula.Application.Common;

/// <summary>
/// Process-wide lock for writes that read before they write (id/number allocation, demo reset).
/// One shared gate keeps a demo reset from running in the middle of such a write.
/// </summary>
public static class WriteGate
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task<T> RunAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            return await action();
        }
        finally
        {
            Gate.Release();
        }
    }

    public static Task RunAsync(Func<Task> action, CancellationToken ct) =>
        RunAsync(async () =>
        {
            await action();
            return true;
        }, ct);
}
