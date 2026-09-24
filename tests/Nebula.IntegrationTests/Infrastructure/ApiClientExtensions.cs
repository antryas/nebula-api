using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Nebula.IntegrationTests.Infrastructure;

public static class ApiClientExtensions
{
    public const string DemoEmail = "alex@nebula.store";
    public const string DemoPassword = "demo1234";

    /// <summary>Creates a client that is signed in with the demo credentials.</summary>
    public static async Task<HttpClient> CreateAuthenticatedClientAsync(this NebulaApiFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = DemoEmail, password = DemoPassword },
            TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.ReadJsonAsync();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", json.GetProperty("token").GetString());
        return client;
    }

    /// <summary>Reads the response body as a detached JSON element.</summary>
    public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        await using var body = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        using var document = await JsonDocument.ParseAsync(body, cancellationToken: TestContext.Current.CancellationToken);
        return document.RootElement.Clone();
    }
}
