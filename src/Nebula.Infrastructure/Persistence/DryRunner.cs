using Nebula.Application.Common;

namespace Nebula.Infrastructure.Persistence;

/// <summary>
/// Wraps the operation in a transaction on the request's <see cref="AppDbContext"/> that is always rolled back.
/// The <see cref="WriteGate"/> is taken first, the same lock order as live orders and demo resets, so a dry run
/// never holds the SQLite write lock while it waits for the gate (services that take the gate re-enter it).
/// </summary>
internal sealed class DryRunner(AppDbContext db, WriteGate writeGate) : IDryRunner
{
    public Task<T> RunAndRollBackAsync<T>(Func<Task<T>> operation, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return writeGate.RunAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            try
            {
                return await operation();
            }
            finally
            {
                // Not cancellable: an aborted request must still leave nothing behind.
                await transaction.RollbackAsync(CancellationToken.None);
                // Tracked entities still hold the discarded changes; nothing later in the request may see them.
                db.ChangeTracker.Clear();
            }
        }, ct);
    }
}
