using System.Net;
using System.Text.Json;
using Nebula.IntegrationTests.Infrastructure;

namespace Nebula.IntegrationTests;

public sealed class HealthTests(NebulaApiFactory factory) : IClassFixture<NebulaApiFactory>
{
    [Fact]
    public async Task Health_returns_healthy()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var body = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        using var json = await JsonDocument.ParseAsync(body, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("Healthy", json.RootElement.GetProperty("status").GetString());
    }
}
