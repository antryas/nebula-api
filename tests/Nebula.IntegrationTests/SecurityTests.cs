using System.Net;
using System.Net.Http.Json;
using Nebula.IntegrationTests.Infrastructure;

namespace Nebula.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class SecurityTests(NebulaApiFactory factory)
{
    [Fact]
    public async Task Preflight_from_allowed_origin_is_accepted()
    {
        using var client = factory.CreateClient();
        using var request = Preflight("http://localhost:4410");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal(
            "http://localhost:4410",
            Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
    }

    [Fact]
    public async Task Preflight_from_unknown_origin_gets_no_cors_headers()
    {
        using var client = factory.CreateClient();
        using var request = Preflight("https://evil.example");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task OpenApi_document_declares_bearer_scheme()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.ReadJsonAsync();
        var scheme = json.GetProperty("components").GetProperty("securitySchemes").GetProperty("bearerAuth");
        Assert.Equal("http", scheme.GetProperty("type").GetString());
        Assert.Equal("bearer", scheme.GetProperty("scheme").GetString());
        Assert.True(json.GetProperty("paths").TryGetProperty("/api/auth/login", out _));
    }

    [Fact]
    public async Task Swagger_ui_is_served()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/swagger/index.html", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Root_redirects_to_swagger()
    {
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        using var response = await client.GetAsync("/", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/swagger", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Server_header_is_not_sent()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.False(response.Headers.Contains("Server"));
    }

    private static HttpRequestMessage Preflight(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/orders");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "authorization");
        return request;
    }
}

public sealed class RateLimitedApiFactory : NebulaApiFactory
{
    protected override IDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        ["RateLimiting:PermitLimit"] = "3",
        ["RateLimiting:WindowSeconds"] = "60",
    };
}

public sealed class RateLimitTests(RateLimitedApiFactory factory) : IClassFixture<RateLimitedApiFactory>
{
    [Fact]
    public async Task Fourth_request_in_window_is_rejected_with_429()
    {
        using var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;
        var login = new { email = ApiClientExtensions.DemoEmail, password = ApiClientExtensions.DemoPassword };

        for (var i = 0; i < 3; i++)
        {
            using var ok = await client.PostAsJsonAsync("/api/auth/login", login, ct);
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }

        using var rejected = await client.PostAsJsonAsync("/api/auth/login", login, ct);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal("application/problem+json", rejected.Content.Headers.ContentType?.MediaType);
        Assert.True(rejected.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        var json = await rejected.ReadJsonAsync();
        Assert.Equal("rate_limited", json.GetProperty("code").GetString());
        Assert.Equal("Too many requests, slow down a little", json.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Health_is_not_rate_limited()
    {
        using var client = factory.CreateClient();

        for (var i = 0; i < 5; i++)
        {
            using var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}
