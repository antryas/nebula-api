using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Nebula.IntegrationTests.Infrastructure;

namespace Nebula.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class AnalyticsEndpointsTests(NebulaApiFactory factory)
{
    public static TheoryData<string> Endpoints =>
        ["overview", "revenue", "categories", "heatmap", "geo", "funnel", "top-products"];

    [Fact]
    public async Task Overview_returns_four_kpis()
    {
        var json = await GetAsync("/api/analytics/overview?range=30d");

        Assert.Equal(["revenue", "orders", "aov", "conversion"], json.EnumerateArray().Select(k => k.GetProperty("key").GetString()));
        foreach (var kpi in json.EnumerateArray())
        {
            Assert.Equal(12, kpi.GetProperty("spark").GetArrayLength());
            Assert.True(kpi.GetProperty("value").GetDecimal() > 0);
            Assert.Equal(JsonValueKind.Number, kpi.GetProperty("previous").ValueKind);
            Assert.Equal(JsonValueKind.Number, kpi.GetProperty("deltaPct").ValueKind);
            Assert.Contains(kpi.GetProperty("format").GetString(), new[] { "currency", "number", "percent" });
            Assert.False(string.IsNullOrEmpty(kpi.GetProperty("label").GetString()));
        }
    }

    [Fact]
    public async Task Revenue_returns_one_point_per_day()
    {
        var json = await GetAsync("/api/analytics/revenue?range=30d");

        Assert.Equal(30, json.GetArrayLength());
        var last = json[json.GetArrayLength() - 1];
        Assert.Equal("2026-09-24", last.GetProperty("date").GetString());
        Assert.Equal(JsonValueKind.Number, last.GetProperty("revenue").ValueKind);
        Assert.Equal(JsonValueKind.Number, last.GetProperty("orders").ValueKind);
    }

    [Fact]
    public async Task Revenue_12m_returns_monthly_points()
    {
        var json = await GetAsync("/api/analytics/revenue?range=12m");

        Assert.Equal(12, json.GetArrayLength());
        Assert.Equal("2026-09-01", json[11].GetProperty("date").GetString());
    }

    [Fact]
    public async Task Categories_use_category_names_and_sum_to_item_revenue()
    {
        var json = await GetAsync("/api/analytics/categories?range=30d");

        Assert.Equal(
            ["Accessories", "Apparel", "Beauty", "Electronics", "Footwear", "Home"],
            json.EnumerateArray().Select(c => c.GetProperty("category").GetString()).Order());
        var revenues = json.EnumerateArray().Select(c => c.GetProperty("revenue").GetDecimal()).ToList();
        Assert.Equal(revenues.OrderByDescending(r => r), revenues);
    }

    [Fact]
    public async Task Heatmap_has_7_by_24_cells()
    {
        var json = await GetAsync("/api/analytics/heatmap?range=30d");

        Assert.Equal(7 * 24, json.GetArrayLength());
        Assert.Equal(0, json[0].GetProperty("weekday").GetInt32());
        Assert.Equal(0, json[0].GetProperty("hour").GetInt32());
        Assert.Equal(6, json[167].GetProperty("weekday").GetInt32());
        Assert.Equal(23, json[167].GetProperty("hour").GetInt32());
        Assert.True(json.EnumerateArray().Sum(c => c.GetProperty("orders").GetInt32()) > 0);
    }

    [Fact]
    public async Task Geo_returns_countries_sorted_by_revenue()
    {
        var json = await GetAsync("/api/analytics/geo?range=30d");

        Assert.True(json.GetArrayLength() > 0);
        var first = json[0];
        Assert.Equal(2, first.GetProperty("countryCode").GetString()!.Length);
        Assert.False(string.IsNullOrEmpty(first.GetProperty("country").GetString()));
        Assert.True(first.GetProperty("orders").GetInt32() > 0);
    }

    [Fact]
    public async Task Funnel_returns_five_steps_in_order()
    {
        var json = await GetAsync("/api/analytics/funnel?range=30d");

        Assert.Equal(
            ["Visits", "Product views", "Added to cart", "Checkout", "Paid"],
            json.EnumerateArray().Select(s => s.GetProperty("step").GetString()));
        var values = json.EnumerateArray().Select(s => s.GetProperty("value").GetInt64()).ToList();
        Assert.Equal(values.OrderByDescending(v => v), values);
    }

    [Fact]
    public async Task Top_products_default_to_five_with_full_product()
    {
        var json = await GetAsync("/api/analytics/top-products?range=30d");

        Assert.Equal(5, json.GetArrayLength());
        var top = json[0];
        Assert.True(top.GetProperty("unitsSold").GetInt32() > 0);
        Assert.True(top.GetProperty("revenue").GetDecimal() > 0);
        var product = top.GetProperty("product");
        foreach (var name in new[]
                 {
                     "id", "sku", "name", "description", "category", "price", "compareAtPrice", "imageUrl",
                     "stock", "sold", "rating", "variants", "createdAt", "active",
                 })
        {
            Assert.True(product.TryGetProperty(name, out _), $"product.{name} is missing");
        }

        Assert.EndsWith("Z", product.GetProperty("createdAt").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Top_products_limit_is_clamped_to_50()
    {
        var json = await GetAsync("/api/analytics/top-products?range=12m&limit=500");

        Assert.InRange(json.GetArrayLength(), 1, 50);
    }

    [Fact]
    public async Task Missing_range_defaults_to_30d()
    {
        var json = await GetAsync("/api/analytics/revenue");

        Assert.Equal(30, json.GetArrayLength());
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Unknown_range_is_422_validation(string endpoint)
    {
        using var client = await factory.CreateAuthenticatedClientAsync();

        using var response = await client.GetAsync($"/api/analytics/{endpoint}?range=5y", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var json = await response.ReadJsonAsync();
        Assert.Equal("validation", json.GetProperty("code").GetString());
        Assert.Equal("Unknown range \"5y\"", json.GetProperty("message").GetString());
        Assert.Equal("Expected one of 7d, 30d, 90d, 12m", json.GetProperty("details").GetProperty("range").GetString());
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Requires_authentication(string endpoint)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/api/analytics/{endpoint}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Twelve_month_range_answers_within_300_ms(string endpoint)
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        var url = $"/api/analytics/{endpoint}?range=12m";
        using (var warmUp = await client.GetAsync(url, TestContext.Current.CancellationToken))
        {
            warmUp.EnsureSuccessStatusCode();
        }

        var stopwatch = Stopwatch.StartNew();
        using var response = await client.GetAsync(url, TestContext.Current.CancellationToken);
        stopwatch.Stop();

        response.EnsureSuccessStatusCode();
        TestContext.Current.TestOutputHelper?.WriteLine($"{endpoint}: {stopwatch.ElapsedMilliseconds} ms");
        Assert.True(stopwatch.ElapsedMilliseconds < 300, $"{endpoint} took {stopwatch.ElapsedMilliseconds} ms");
    }

    private async Task<JsonElement> GetAsync(string url)
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        using var response = await client.GetAsync(url, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync();
    }
}
