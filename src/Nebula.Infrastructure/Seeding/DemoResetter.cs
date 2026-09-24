using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Nebula.Application.Common;

namespace Nebula.Infrastructure.Seeding;

internal sealed partial class DemoResetter(DatabaseInitializer initializer, ILogger<DemoResetter> logger) : IDemoResetter
{
    // Static: resets are serialized process-wide, whichever scope triggers them.
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task ResetAsync(CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var stopwatch = Stopwatch.StartNew();
            await initializer.ResetAsync(ct);
            LogReset(stopwatch.ElapsedMilliseconds);
        }
        finally
        {
            Gate.Release();
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Demo data reset in {ElapsedMs} ms")]
    private partial void LogReset(long elapsedMs);
}
