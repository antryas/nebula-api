using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Nebula.Infrastructure.Ai;
using Nebula.IntegrationTests.Infrastructure;

namespace Nebula.IntegrationTests;

internal static class AiTestData
{
    public const string TopProducts = "What were my top 5 products this month?";
    public const string Revenue = "How did revenue change compared to the previous period?";
    public const string Waiting = "Which orders are still waiting to be shipped?";
    public const string Customers = "Who are my most valuable customers?";

    public const string Fallback =
        "Live AI is not available right now. Try one of the suggested questions — they are answered from live store data.";

    public static readonly object Description = new
    {
        name = "Nebula Everyday Hoodie",
        category = "Apparel",
        keywords = "brushed fleece, relaxed fit",
        tone = "friendly",
    };

    public static async Task<JsonElement> PostJsonAsync(this HttpClient client, string url, object body, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var response = await client.PostAsJsonAsync(url, body, TestContext.Current.CancellationToken);
        Assert.Equal(expected, response.StatusCode);
        return await response.ReadJsonAsync();
    }

    public static async Task<JsonElement> GetJsonAsync(this HttpClient client, string url)
    {
        using var response = await client.GetAsync(url, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync();
    }

    public static string[] Names(JsonElement element) => [.. element.EnumerateObject().Select(p => p.Name)];

    public static string[] Strings(JsonElement array) => [.. array.EnumerateArray().Select(e => e.GetString()!)];
}

/// <summary>No API key configured (the default): every endpoint works in recorded mode from real store data.</summary>
[Collection(ApiCollection.Name)]
public sealed class AiRecordedEndpointsTests(NebulaApiFactory factory) : IAsyncLifetime
{
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync() => _client = await factory.CreateAuthenticatedClientAsync();

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Status_reports_disabled_without_a_key()
    {
        var status = await _client.GetJsonAsync("/api/ai/status");

        Assert.Equal(["enabled", "provider", "quota"], AiTestData.Names(status));
        Assert.False(status.GetProperty("enabled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, status.GetProperty("provider").ValueKind);
        Assert.Equal(["remaining", "limit"], AiTestData.Names(status.GetProperty("quota")));
        Assert.Equal(0, status.GetProperty("quota").GetProperty("remaining").GetInt32());
        Assert.Equal(20, status.GetProperty("quota").GetProperty("limit").GetInt32());
    }

    [Fact]
    public async Task Top_products_answer_quotes_the_analytics_numbers()
    {
        var top = (await _client.GetJsonAsync("/api/analytics/top-products?range=30d&limit=5")).EnumerateArray().ToList();

        var answer = await _client.PostJsonAsync("/api/ai/ask", new { question = AiTestData.TopProducts });

        Assert.Equal(["answer", "mode", "toolsUsed", "quota"], AiTestData.Names(answer));
        Assert.Equal("recorded", answer.GetProperty("mode").GetString());
        Assert.Equal(["get_top_products"], AiTestData.Strings(answer.GetProperty("toolsUsed")));
        var text = answer.GetProperty("answer").GetString()!;
        Assert.Equal(5, top.Count);
        foreach (var row in top)
        {
            Assert.Contains($"**{row.GetProperty("product").GetProperty("name").GetString()}**", text, StringComparison.Ordinal);
        }

        Assert.Contains(top[0].GetProperty("revenue").GetDecimal().ToString("N2", System.Globalization.CultureInfo.InvariantCulture), text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Revenue_answer_quotes_the_overview_kpi()
    {
        var revenue = (await _client.GetJsonAsync("/api/analytics/overview?range=30d")).EnumerateArray()
            .Single(k => k.GetProperty("key").GetString() == "revenue");

        var answer = await _client.PostJsonAsync("/api/ai/ask", new { question = AiTestData.Revenue, history = Array.Empty<object>() });

        Assert.Equal(["get_sales_overview"], AiTestData.Strings(answer.GetProperty("toolsUsed")));
        var text = answer.GetProperty("answer").GetString()!;
        Assert.Contains(
            "$" + revenue.GetProperty("value").GetDecimal().ToString("N2", System.Globalization.CultureInfo.InvariantCulture),
            text,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Waiting_orders_answer_counts_new_and_packing_orders()
    {
        var page = await _client.GetJsonAsync("/api/orders?status=new,packing&pageSize=1");

        var answer = await _client.PostJsonAsync("/api/ai/ask", new { question = "which orders are waiting to be SHIPPED" });

        Assert.Equal(["list_orders"], AiTestData.Strings(answer.GetProperty("toolsUsed")));
        Assert.StartsWith($"**{page.GetProperty("total").GetInt32().ToString("N0", System.Globalization.CultureInfo.InvariantCulture)}**", answer.GetProperty("answer").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Valuable_customers_answer_lists_the_highest_lifetime_value()
    {
        var best = (await _client.GetJsonAsync("/api/customers?sort=lifetimeValue&dir=desc&pageSize=1"))
            .GetProperty("items")[0].GetProperty("name").GetString();

        var answer = await _client.PostJsonAsync("/api/ai/ask", new { question = AiTestData.Customers });

        Assert.Equal(["get_top_customers"], AiTestData.Strings(answer.GetProperty("toolsUsed")));
        Assert.Contains($"**{best}**", answer.GetProperty("answer").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Other_questions_get_the_fixed_fallback()
    {
        var answer = await _client.PostJsonAsync("/api/ai/ask", new { question = "Tell me a joke" });

        Assert.Equal(AiTestData.Fallback, answer.GetProperty("answer").GetString());
        Assert.Equal("recorded", answer.GetProperty("mode").GetString());
        Assert.Empty(answer.GetProperty("toolsUsed").EnumerateArray());
        Assert.Equal(0, answer.GetProperty("quota").GetProperty("remaining").GetInt32());
    }

    [Fact]
    public async Task Product_description_uses_the_tone_template()
    {
        var result = await _client.PostJsonAsync("/api/ai/product-description", AiTestData.Description);

        Assert.Equal(["description", "mode", "quota"], AiTestData.Names(result));
        Assert.Equal("recorded", result.GetProperty("mode").GetString());
        var text = result.GetProperty("description").GetString()!;
        Assert.StartsWith("Meet the Nebula Everyday Hoodie", text, StringComparison.Ordinal);
        Assert.Contains("brushed fleece and relaxed fit", text, StringComparison.Ordinal);
        Assert.InRange(text.Length, 1, 600);
    }

    public static TheoryData<object, string> InvalidQuestions => new()
    {
        { new { question = "" }, "question" },
        { new { question = "   " }, "question" },
        { new { }, "question" },
        { new { question = new string('x', 501) }, "question" },
        { new { question = "Hi", history = Enumerable.Repeat(new { role = "user", content = "x" }, 7).ToArray() }, "history" },
        { new { question = "Hi", history = new[] { new { role = "system", content = "x" } } }, "history[0].role" },
        { new { question = "Hi", history = new[] { new { role = "user", content = new string('x', 2001) } } }, "history[0].content" },
    };

    [Theory]
    [MemberData(nameof(InvalidQuestions))]
    public async Task Invalid_questions_are_400_validation(object body, string field)
    {
        var problem = await _client.PostJsonAsync("/api/ai/ask", body, HttpStatusCode.BadRequest);

        Assert.Equal(400, problem.GetProperty("status").GetInt32());
        Assert.Equal("validation", problem.GetProperty("code").GetString());
        Assert.True(problem.GetProperty("details").TryGetProperty(field, out _), $"no details for {field}");
    }

    public static TheoryData<object, string> InvalidDescriptions => new()
    {
        { new { name = "", category = "Apparel", tone = "friendly" }, "name" },
        { new { name = new string('x', 121), category = "Apparel", tone = "friendly" }, "name" },
        { new { name = "Hoodie", category = "apparel", tone = "friendly" }, "category" },
        { new { name = "Hoodie", category = "Apparel", keywords = new string('k', 201), tone = "friendly" }, "keywords" },
        { new { name = "Hoodie", category = "Apparel", tone = "formal" }, "tone" },
        { new { name = "Hoodie", category = "Apparel" }, "tone" },
    };

    [Theory]
    [MemberData(nameof(InvalidDescriptions))]
    public async Task Invalid_description_requests_are_400_validation(object body, string field)
    {
        var problem = await _client.PostJsonAsync("/api/ai/product-description", body, HttpStatusCode.BadRequest);

        Assert.Equal("validation", problem.GetProperty("code").GetString());
        Assert.True(problem.GetProperty("details").TryGetProperty(field, out _), $"no details for {field}");
    }

    [Fact]
    public async Task Null_body_is_400_validation()
    {
        using var content = new StringContent("null", System.Text.Encoding.UTF8, "application/json");
        using var response = await _client.PostAsync("/api/ai/ask", content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation", (await response.ReadJsonAsync()).GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("GET", "/api/ai/status")]
    [InlineData("POST", "/api/ai/ask")]
    [InlineData("POST", "/api/ai/product-description")]
    public async Task Endpoints_require_a_token(string method, string url)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method == "POST")
        {
            request.Content = JsonContent.Create(new { question = "Hi" });
        }

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

/// <summary>An API key and a fake provider: the live path through the real function-invocation pipeline.</summary>
public class AiLiveApiFactory : NebulaApiFactory
{
    public FakeChatClient Provider { get; } = new();

    protected override IDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        ["Ai:ApiKey"] = "test-key",
        ["Ai:DailyRequestsPerClient"] = "100",
        ["Ai:DailyRequestsTotal"] = "1000",
        ["Ai:MaxOutputTokens"] = "999999",
        ["Ai:TimeoutSeconds"] = "1",
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services => services.AddKeyedSingleton<IChatClient>(AiClientSetup.ProviderClientKey, Provider));
    }
}

public sealed class AiLiveEndpointsTests(AiLiveApiFactory factory) : IClassFixture<AiLiveApiFactory>, IAsyncLifetime
{
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync() => _client = await factory.CreateAuthenticatedClientAsync();

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Status_reports_the_provider_and_quota()
    {
        var status = await _client.GetJsonAsync("/api/ai/status");

        Assert.True(status.GetProperty("enabled").GetBoolean());
        Assert.Equal("DeepSeek", status.GetProperty("provider").GetString());
        Assert.Equal(100, status.GetProperty("quota").GetProperty("limit").GetInt32());
    }

    [Fact]
    public async Task Live_answer_runs_the_tool_and_reports_it()
    {
        var top = (await _client.GetJsonAsync("/api/analytics/top-products?range=30d&limit=1"))[0];
        var before = (await _client.GetJsonAsync("/api/ai/status")).GetProperty("quota").GetProperty("remaining").GetInt32();

        var answer = await _client.PostJsonAsync("/api/ai/ask", new
        {
            question = "Which product is on top?",
            history = new[] { new { role = "user", content = "Hi" }, new { role = "assistant", content = "Hello!" } },
        });

        Assert.Equal("live", answer.GetProperty("mode").GetString());
        Assert.Equal(["get_top_products"], AiTestData.Strings(answer.GetProperty("toolsUsed")));
        var text = answer.GetProperty("answer").GetString()!;
        Assert.StartsWith("From the store data:", text, StringComparison.Ordinal);
        Assert.Contains(top.GetProperty("product").GetProperty("name").GetString()!, text, StringComparison.Ordinal);
        Assert.Contains("\"unitsSold\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("imageUrl", text, StringComparison.Ordinal);
        // One question costs one unit, however many tool round trips it takes.
        Assert.True(answer.GetProperty("quota").GetProperty("remaining").GetInt32() <= before - 1);
    }

    [Fact]
    public async Task Live_answer_without_tools_has_empty_tools_used_and_clamped_output_budget()
    {
        var answer = await _client.PostJsonAsync("/api/ai/ask", new { question = "How is the store doing?" });

        Assert.Equal("live", answer.GetProperty("mode").GetString());
        Assert.Equal(FakeChatClient.PlainAnswer, answer.GetProperty("answer").GetString());
        Assert.Empty(answer.GetProperty("toolsUsed").EnumerateArray());
        Assert.All(factory.Provider.Calls, options => Assert.InRange(options!.MaxOutputTokens ?? 0, 1, 2000));
    }

    [Fact]
    public async Task Provider_failure_falls_back_to_recorded_without_leaking_details()
    {
        var answer = await _client.PostJsonAsync("/api/ai/ask", new { question = "Why did it fail?" });

        Assert.Equal("recorded", answer.GetProperty("mode").GetString());
        Assert.Equal(AiTestData.Fallback, answer.GetProperty("answer").GetString());
        Assert.DoesNotContain("secret", answer.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Provider_timeout_falls_back_to_recorded()
    {
        var answer = await _client.PostJsonAsync("/api/ai/ask", new { question = "Revenue is slow?" });

        Assert.Equal("recorded", answer.GetProperty("mode").GetString());
        Assert.Equal(["get_sales_overview"], AiTestData.Strings(answer.GetProperty("toolsUsed")));
    }

    [Fact]
    public async Task Live_description_is_flattened_plain_text()
    {
        var result = await _client.PostJsonAsync("/api/ai/product-description", AiTestData.Description);

        Assert.Equal("live", result.GetProperty("mode").GetString());
        Assert.Equal("Cozy fleece for cold days. It pairs with everything.", result.GetProperty("description").GetString());
    }
}

/// <summary>One live request per client per day: the second one is answered in recorded mode.</summary>
public class AiQuotaApiFactory : AiLiveApiFactory
{
    protected override IDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        ["Ai:ApiKey"] = "test-key",
        ["Ai:DailyRequestsPerClient"] = "1",
        ["Ai:DailyRequestsTotal"] = "1000",
    };
}

public sealed class AiQuotaTests(AiQuotaApiFactory factory) : IClassFixture<AiQuotaApiFactory>
{
    [Fact]
    public async Task Exhausted_quota_switches_to_recorded_mode()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();

        var first = await client.PostJsonAsync("/api/ai/ask", new { question = AiTestData.Revenue });
        Assert.Equal("live", first.GetProperty("mode").GetString());
        Assert.Equal(0, first.GetProperty("quota").GetProperty("remaining").GetInt32());

        var calls = factory.Provider.Calls.Count;
        var second = await client.PostJsonAsync("/api/ai/ask", new { question = AiTestData.Revenue });
        var description = await client.PostJsonAsync("/api/ai/product-description", AiTestData.Description);
        var status = await client.GetJsonAsync("/api/ai/status");

        Assert.Equal("recorded", second.GetProperty("mode").GetString());
        Assert.Equal(["get_sales_overview"], AiTestData.Strings(second.GetProperty("toolsUsed")));
        Assert.Equal(0, second.GetProperty("quota").GetProperty("remaining").GetInt32());
        Assert.Equal(1, second.GetProperty("quota").GetProperty("limit").GetInt32());
        Assert.Equal("recorded", description.GetProperty("mode").GetString());
        Assert.True(status.GetProperty("enabled").GetBoolean());
        Assert.Equal(0, status.GetProperty("quota").GetProperty("remaining").GetInt32());
        Assert.Equal(calls, factory.Provider.Calls.Count);
    }
}

public sealed class AiRateLimitedApiFactory : NebulaApiFactory
{
    protected override IDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        ["RateLimiting:AiPermitLimit"] = "2",
    };
}

public sealed class AiRateLimitTests(AiRateLimitedApiFactory factory) : IClassFixture<AiRateLimitedApiFactory>
{
    [Fact]
    public async Task Ai_endpoints_have_their_own_tighter_limit()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        var ct = TestContext.Current.CancellationToken;

        for (var i = 0; i < 2; i++)
        {
            using var ok = await client.GetAsync("/api/ai/status", ct);
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }

        using var rejected = await client.GetAsync("/api/ai/status", ct);
        using var other = await client.GetAsync("/api/customers?pageSize=1", ct);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal("rate_limited", (await rejected.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
    }
}
