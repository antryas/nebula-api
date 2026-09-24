using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Nebula.Application.Common;

namespace Nebula.Infrastructure.Seeding;

internal sealed partial class DemoResetter(DatabaseInitializer initializer, ILogger<DemoResetter> logger) : IDemoResetter
{
    // Shares the process-wide WriteGate so a reset never interleaves with live orders or product creates.
    public Task ResetAsync(CancellationToken ct = default) =>
        WriteGate.RunAsync(async () =>
        {
            var stopwatch = Stopwatch.StartNew();
            await initializer.ResetAsync(ct);
            LogReset(stopwatch.ElapsedMilliseconds);
        }, ct);

    [LoggerMessage(Level = LogLevel.Information, Message = "Demo data reset in {ElapsedMs} ms")]
    private partial void LogReset(long elapsedMs);
}
