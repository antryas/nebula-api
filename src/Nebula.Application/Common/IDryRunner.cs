namespace Nebula.Application.Common;

/// <summary>
/// Runs a write for real and then discards it: the operation sees and returns exactly what a real write would,
/// but nothing it saved is persisted. Backs the read-only public demo.
/// </summary>
public interface IDryRunner
{
    Task<T> RunAndRollBackAsync<T>(Func<Task<T>> operation, CancellationToken ct = default);
}
