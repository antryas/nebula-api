using System.Net;
using System.Text;
using System.Text.Json;
using Nebula.IntegrationTests.Infrastructure;

namespace Nebula.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class ErrorShapeTests(NebulaApiFactory factory)
{
    [Fact]
    public async Task Unknown_api_route_returns_404_not_found()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();

        using var response = await client.GetAsync("/api/nope", TestContext.Current.CancellationToken);

        var json = await AssertProblemAsync(response, HttpStatusCode.NotFound, "not_found");
        Assert.Equal("Route not found", json.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Missing_token_returns_401_unauthorized()
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/api/demo/reset", null, TestContext.Current.CancellationToken);

        var json = await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "unauthorized");
        Assert.Equal("Sign in to use the API", json.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Malformed_json_returns_400_bad_request()
    {
        using var client = factory.CreateClient();
        using var content = new StringContent("{bad json", Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/api/auth/login", content, TestContext.Current.CancellationToken);

        var json = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "bad_request");
        Assert.Equal("Request body is not valid JSON", json.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Oversized_body_returns_413_payload_too_large()
    {
        using var client = factory.CreateClient();
        var body = "{\"email\":\"" + new string('a', 70 * 1024) + "\",\"password\":\"demo1234\"}";
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/api/auth/login", content, TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.RequestEntityTooLarge, "payload_too_large");
    }

    [Fact]
    public async Task Wrong_content_type_returns_415_unsupported_media_type()
    {
        using var client = factory.CreateClient();
        using var content = new StringContent("email=a", Encoding.UTF8, "text/plain");

        using var response = await client.PostAsync("/api/auth/login", content, TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.UnsupportedMediaType, "unsupported_media_type");
    }

    [Fact]
    public async Task Wrong_method_returns_405_method_not_allowed()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();

        using var response = await client.GetAsync("/api/auth/login", TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.MethodNotAllowed, "method_not_allowed");
    }

    [Fact]
    public async Task Empty_status_code_outside_api_gets_problem_body()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/does-not-exist", TestContext.Current.CancellationToken);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "not_found");
    }

    private static async Task<JsonElement> AssertProblemAsync(
        HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var json = await response.ReadJsonAsync();
        Assert.Equal(JsonValueKind.Number, json.GetProperty("status").ValueKind);
        Assert.Equal((int)status, json.GetProperty("status").GetInt32());
        Assert.Equal(code, json.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("message").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("title").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("traceId").GetString()));
        return json;
    }
}
