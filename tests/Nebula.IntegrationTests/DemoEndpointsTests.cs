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
    }
}
