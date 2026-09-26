using System.Net;
using Nebula.IntegrationTests.Infrastructure;

namespace Nebula.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class DemoEndpointsTests(NebulaApiFactory factory)
{
    [Fact]
    public async Task Reset_returns_204_for_signed_in_user()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();

        using var response = await client.PostAsync("/api/demo/reset", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(response.Headers.Contains("X-Nebula-Dry-Run"));
    }

    [Fact]
    public async Task Mode_is_writable_when_read_only_is_off()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();

        using var response = await client.GetAsync("/api/demo/mode", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False((await response.ReadJsonAsync()).GetProperty("readOnly").GetBoolean());
    }
}
