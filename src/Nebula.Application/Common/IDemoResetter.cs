namespace Nebula.Application.Common;

/// <summary>Restores the demo database to its deterministic seed. Concurrent calls are serialized.</summary>
public interface IDemoResetter
{
    Task ResetAsync(CancellationToken ct = default);
}
