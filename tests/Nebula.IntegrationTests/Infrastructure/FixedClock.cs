using Nebula.Application.Common;

namespace Nebula.IntegrationTests.Infrastructure;

public sealed class FixedClock(DateTime now) : IClock
{
    public DateTime UtcNow { get; set; } = now;
}
