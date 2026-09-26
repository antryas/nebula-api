namespace Nebula.Application.Common;

/// <summary>
/// The app's single lock for database writes (id/number allocation, demo reset, visitor writes, dry runs).
/// Every writer takes it before it touches SQLite, so writers never wait on each other's SQLite write lock and a
/// demo reset cannot run in the middle of another write. Registered as a singleton: one gate per app and database.
/// </summary>
/// <remarks>
/// Re-entrant within one async flow, so a dry run can hold it around services that take it themselves. Re-entry is
/// tied to the current hold: a flow that outlives it (e.g. work started inside the gate that is still running
/// afterwards) waits like everyone else.
/// </remarks>
public sealed class WriteGate
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly AsyncLocal<object?> _flowHold = new();
    private object? _currentHold;

    public async Task<T> RunAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (_flowHold.Value is { } held && ReferenceEquals(held, Volatile.Read(ref _currentHold)))
        {
            return await action();
        }

        await _gate.WaitAsync(ct);
        var hold = new object();
        Volatile.Write(ref _currentHold, hold);
        // Flows into the awaited action only; the caller's flow is restored when this method returns.
        _flowHold.Value = hold;
        try
        {
            return await action();
        }
        finally
        {
            Volatile.Write(ref _currentHold, null);
            _gate.Release();
        }
    }

    public Task RunAsync(Func<Task> action, CancellationToken ct) =>
        RunAsync(async () =>
        {
            await action();
            return true;
        }, ct);
}
