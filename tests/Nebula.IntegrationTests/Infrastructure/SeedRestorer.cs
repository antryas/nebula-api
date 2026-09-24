using Microsoft.Extensions.DependencyInjection;
using Nebula.Application.Common;

namespace Nebula.IntegrationTests.Infrastructure;

/// <summary>
/// Class fixture for test classes in <see cref="ApiCollection"/> that change data: once the class is done
/// it restores the deterministic seed, so later classes see a pristine shared database.
/// </summary>
public sealed class SeedRestorer(NebulaApiFactory factory) : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IDemoResetter>().ResetAsync();
    }
}
