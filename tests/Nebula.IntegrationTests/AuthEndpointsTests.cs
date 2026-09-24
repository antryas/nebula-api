using System.Net;
using System.Net.Http.Json;
using System.Text;
using Nebula.IntegrationTests.Infrastructure;

namespace Nebula.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class AuthEndpointsTests(NebulaApiFactory factory)
{
    [Fact]
    public async Task Login_with_demo_credentials_returns_token_and_user()
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = ApiClientExtensions.DemoEmail, password = ApiClientExtensions.DemoPassword },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.ReadJsonAsync();
        Assert.Equal(3, json.GetProperty("token").GetString()!.Split('.').Length);
        var user = json.GetProperty("user");
        Assert.Equal("usr_1", user.GetProperty("id").GetString());
        Assert.Equal("Alex Morgan", user.GetProperty("name").GetString());
        Assert.Equal("alex@nebula.store", user.GetProperty("email").GetString());
        Assert.Equal("Admin", user.GetProperty("role").GetString());
        Assert.False(string.IsNullOrEmpty(user.GetProperty("avatarUrl").GetString()));
    }

    [Theory]
    [InlineData("{\"email\":\"alex@nebula.store\",\"password\":\"123\"}")]
    [InlineData("{\"email\":\"   \",\"password\":\"demo1234\"}")]
    [InlineData("{}")]
    [InlineData("")]
    public async Task Login_with_invalid_credentials_returns_401(string body)
    {
        using var client = factory.CreateClient();
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/api/auth/login", content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var json = await response.ReadJsonAsync();
        Assert.Equal("invalid_credentials", json.GetProperty("code").GetString());
        Assert.Equal("Invalid email or password", json.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Tampered_token_is_rejected()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        var token = client.DefaultRequestHeaders.Authorization!.Parameter!;
        var tampered = token[..^2] + (token[^2..] == "AA" ? "BB" : "AA");
        client.DefaultRequestHeaders.Authorization = new("Bearer", tampered);

        using var response = await client.PostAsync("/api/demo/reset", null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
