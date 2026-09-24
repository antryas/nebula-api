using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Nebula.IntegrationTests.Infrastructure;

namespace Nebula.IntegrationTests;

/// <summary>Read-only product catalog queries against the seed.</summary>
[Collection(ApiCollection.Name)]
public sealed class ProductsEndpointsTests(NebulaApiFactory factory) : IAsyncLifetime
{
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync() => _client = await factory.CreateAuthenticatedClientAsync();

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<JsonElement> GetJsonAsync(string url)
    {
        using var response = await _client.GetAsync(url, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync();
    }

    private static List<JsonElement> Items(JsonElement page) => [.. page.GetProperty("items").EnumerateArray()];

    [Fact]
    public async Task Default_list_is_first_page_of_all_products_newest_first()
    {
        var page = await GetJsonAsync("/api/products");

        Assert.Equal(60, page.GetProperty("total").GetInt32());
        Assert.Equal(1, page.GetProperty("page").GetInt32());
        Assert.Equal(20, page.GetProperty("pageSize").GetInt32());
        var items = Items(page);
        Assert.Equal(20, items.Count);
        var created = items.Select(i => i.GetProperty("createdAt").GetDateTime()).ToList();
        Assert.Equal(created.OrderDescending(), created);
    }

    [Fact]
    public async Task Product_json_has_the_frontend_shape()
    {
        var product = await GetJsonAsync("/api/products/prd_0001");

        Assert.Equal("prd_0001", product.GetProperty("id").GetString());
        Assert.Matches("^[A-Z]{3}-[A-Z]{3}-001$", product.GetProperty("sku").GetString());
        Assert.True(product.TryGetProperty("compareAtPrice", out _));
        Assert.EndsWith("Z", product.GetProperty("createdAt").GetString(), StringComparison.Ordinal);
        var variants = product.GetProperty("variants").EnumerateArray().ToList();
        Assert.NotEmpty(variants);
        Assert.Equal("prd_0001_v1", variants[0].GetProperty("id").GetString());
        Assert.Equal(
            variants.Sum(v => v.GetProperty("stock").GetInt32()),
            product.GetProperty("stock").GetInt32());
        Assert.Contains(
            product.GetProperty("category").GetString(),
            new[] { "Apparel", "Footwear", "Accessories", "Electronics", "Home", "Beauty" });
    }

    [Fact]
    public async Task Filters_by_category()
    {
        var page = await GetJsonAsync("/api/products?category=Footwear&pageSize=100");

        var total = page.GetProperty("total").GetInt32();
        Assert.InRange(total, 1, 59);
        Assert.Equal(total, Items(page).Count);
        Assert.All(Items(page), p => Assert.Equal("Footwear", p.GetProperty("category").GetString()));
    }

    [Theory]
    [InlineData("low", 1, 10)]
    [InlineData("out", 0, 0)]
    [InlineData("in", 11, int.MaxValue)]
    public async Task Filters_by_stock_level(string stock, int min, int max)
    {
        var page = await GetJsonAsync($"/api/products?stock={stock}&pageSize=100");

        Assert.Equal(page.GetProperty("total").GetInt32(), Items(page).Count);
        Assert.All(Items(page), p => Assert.InRange(p.GetProperty("stock").GetInt32(), min, max));
    }

    [Fact]
    public async Task Stock_levels_partition_the_catalog()
    {
        var totals = new List<int>();
        foreach (var stock in new[] { "in", "low", "out" })
        {
            totals.Add((await GetJsonAsync($"/api/products?stock={stock}&pageSize=1")).GetProperty("total").GetInt32());
        }

        Assert.Equal(60, totals.Sum());
        Assert.True(totals[1] > 0, "the seed has low-stock products");
    }

    [Fact]
    public async Task Searches_name_and_sku()
    {
        var first = await GetJsonAsync("/api/products/prd_0001");
        var sku = first.GetProperty("sku").GetString()!;

        var page = await GetJsonAsync($"/api/products?search={Uri.EscapeDataString(sku.ToLowerInvariant())}");

        Assert.Contains(Items(page), p => p.GetProperty("id").GetString() == "prd_0001");
        Assert.All(Items(page), p => Assert.Contains(
            sku, p.GetProperty("sku").GetString() + " " + p.GetProperty("name").GetString(), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Sorts_by_price_ascending()
    {
        var page = await GetJsonAsync("/api/products?sort=price&dir=asc&pageSize=100");

        var prices = Items(page).Select(p => p.GetProperty("price").GetDecimal()).ToList();
        Assert.Equal(prices.Order(), prices);
    }

    [Fact]
    public async Task Get_unknown_product_is_404()
    {
        using var response = await _client.GetAsync("/api/products/prd_9999", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.ReadJsonAsync();
        Assert.Equal("not_found", body.GetProperty("code").GetString());
        Assert.Equal("Product not found", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Requires_authentication()
    {
        using var anonymous = factory.CreateClient();

        using var response = await anonymous.GetAsync("/api/products", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

/// <summary>Mutating tests: <see cref="SeedRestorer"/> restores the seed when the class is done.</summary>
[Collection(ApiCollection.Name)]
public sealed class ProductsMutationEndpointsTests(NebulaApiFactory factory) : IClassFixture<SeedRestorer>, IAsyncLifetime
{
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync() => _client = await factory.CreateAuthenticatedClientAsync();

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The body <c>toProductInput</c> in the Angular product form sends.</summary>
    private static Dictionary<string, object?> Body(string sku) => new()
    {
        ["sku"] = sku,
        ["name"] = "Integration Parka",
        ["description"] = "Warm.",
        ["category"] = "Apparel",
        ["price"] = 129.5,
        ["compareAtPrice"] = 159,
        ["imageUrl"] = "",
        ["stock"] = 0,
        ["variants"] = new object[]
        {
            new { id = "", size = "M", color = "Olive", stock = 4 },
            new { id = "", size = "L", color = "Olive", stock = 3 },
        },
        ["active"] = true,
    };

    private async Task<JsonElement> CreateAsync(string sku)
    {
        using var response = await _client.PostAsJsonAsync("/api/products", Body(sku), Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadJsonAsync();
    }

    private static async Task<JsonElement> AssertValidationAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.ReadJsonAsync();
        Assert.Equal("validation", body.GetProperty("code").GetString());
        Assert.Equal("Product is invalid", body.GetProperty("message").GetString());
        return body.TryGetProperty("details", out var details) ? details : default;
    }

    [Fact]
    public async Task Create_returns_201_with_location_and_server_fields()
    {
        using var response = await _client.PostAsJsonAsync("/api/products", Body("INT-PRK-001"), Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var product = await response.ReadJsonAsync();
        var id = product.GetProperty("id").GetString()!;
        Assert.Matches("^prd_\\d{6}$", id);
        Assert.Equal($"/api/products/{id}", response.Headers.Location?.OriginalString);
        Assert.Equal(7, product.GetProperty("stock").GetInt32());
        Assert.Equal(0, product.GetProperty("sold").GetInt32());
        Assert.Equal(0, product.GetProperty("rating").GetDouble());
        Assert.Equal($"https://picsum.photos/seed/nebula-{id}/400/400", product.GetProperty("imageUrl").GetString());
        Assert.Equal(
            [$"{id}_v1", $"{id}_v2"],
            product.GetProperty("variants").EnumerateArray().Select(v => v.GetProperty("id").GetString()));
        Assert.Equal(NebulaApiFactory.Now, product.GetProperty("createdAt").GetDateTime().ToUniversalTime());

        using var get = await _client.GetAsync(response.Headers.Location, Ct);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var newest = await _client.GetFromJsonAsync<JsonElement>("/api/products?pageSize=1", Ct);
        Assert.Equal(id, newest.GetProperty("items")[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task Invalid_create_is_422_with_field_details()
    {
        var body = Body("INT-BAD-001");
        body["name"] = " ";
        body["price"] = 0;
        body["category"] = "Toys";

        using var response = await _client.PostAsJsonAsync("/api/products", body, Ct);

        var details = await AssertValidationAsync(response);
        Assert.Equal("Name is required", details.GetProperty("name").GetString());
        Assert.Equal("Price must be greater than 0", details.GetProperty("price").GetString());
        Assert.Equal("Unknown category", details.GetProperty("category").GetString());
    }

    [Fact]
    public async Task Compare_at_price_must_exceed_price()
    {
        var body = Body("INT-CMP-001");
        body["compareAtPrice"] = 100;

        using var response = await _client.PostAsJsonAsync("/api/products", body, Ct);

        var details = await AssertValidationAsync(response);
        Assert.Equal(
            "Compare-at price must be greater than price", details.GetProperty("compareAtPrice").GetString());
    }

    [Fact]
    public async Task Duplicate_sku_is_422_on_sku()
    {
        var seeded = await _client.GetFromJsonAsync<JsonElement>("/api/products/prd_0002", Ct);

        using var response = await _client.PostAsJsonAsync(
            "/api/products", Body(seeded.GetProperty("sku").GetString()!.ToLowerInvariant()), Ct);

        var details = await AssertValidationAsync(response);
        Assert.Equal("SKU is already in use", details.GetProperty("sku").GetString());
    }

    [Fact]
    public async Task Null_body_is_422_without_details()
    {
        using var content = new StringContent("null", System.Text.Encoding.UTF8, "application/json");

        using var response = await _client.PostAsync("/api/products", content, Ct);

        Assert.Equal(JsonValueKind.Undefined, (await AssertValidationAsync(response)).ValueKind);
    }

    [Fact]
    public async Task Update_replaces_editable_fields()
    {
        var created = await CreateAsync("INT-UPD-001");
        var id = created.GetProperty("id").GetString()!;
        var body = Body("INT-UPD-001");
        body["name"] = "Renamed Parka";
        body["variants"] = new object[] { new { id = $"{id}_v2", size = "XL", color = "Black", stock = 12 } };
        body["active"] = false;

        using var response = await _client.PutAsJsonAsync($"/api/products/{id}", body, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.ReadJsonAsync();
        Assert.Equal("Renamed Parka", updated.GetProperty("name").GetString());
        Assert.Equal(12, updated.GetProperty("stock").GetInt32());
        Assert.False(updated.GetProperty("active").GetBoolean());
        Assert.Equal(created.GetProperty("createdAt").GetString(), updated.GetProperty("createdAt").GetString());
        Assert.Equal(created.GetProperty("imageUrl").GetString(), updated.GetProperty("imageUrl").GetString());
        var fetched = await _client.GetFromJsonAsync<JsonElement>($"/api/products/{id}", Ct);
        Assert.Equal($"{id}_v2", fetched.GetProperty("variants")[0].GetProperty("id").GetString());
        Assert.Equal(1, fetched.GetProperty("variants").GetArrayLength());
    }

    [Fact]
    public async Task Invalid_update_is_422_and_unknown_update_is_404()
    {
        var body = Body("INT-UPD-404");
        body["name"] = null;
        using var invalid = await _client.PutAsJsonAsync("/api/products/prd_0003", body, Ct);
        var details = await AssertValidationAsync(invalid);
        Assert.Equal("Name is required", details.GetProperty("name").GetString());

        using var missing = await _client.PutAsJsonAsync("/api/products/prd_9999", Body("INT-UPD-404"), Ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Delete_is_204_then_404()
    {
        var created = await CreateAsync("INT-DEL-001");
        var id = created.GetProperty("id").GetString();

        using var deleted = await _client.DeleteAsync($"/api/products/{id}", Ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        using var get = await _client.GetAsync($"/api/products/{id}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        var body = await get.ReadJsonAsync();
        Assert.Equal("Product not found", body.GetProperty("message").GetString());

        using var again = await _client.DeleteAsync($"/api/products/{id}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    [Fact]
    public async Task Deleting_a_sold_product_keeps_orders_and_analytics_working()
    {
        var top = await _client.GetFromJsonAsync<JsonElement>("/api/analytics/top-products?range=12m&limit=3", Ct);
        var bestSeller = top[0].GetProperty("product").GetProperty("id").GetString();

        using var deleted = await _client.DeleteAsync($"/api/products/{bestSeller}", Ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        using var after = await _client.GetAsync("/api/analytics/top-products?range=12m&limit=3", Ct);
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        var afterJson = await after.ReadJsonAsync();
        Assert.Equal(3, afterJson.GetArrayLength());
        Assert.DoesNotContain(afterJson.EnumerateArray(), t => t.GetProperty("product").GetProperty("id").GetString() == bestSeller);

        var orders = await _client.GetFromJsonAsync<JsonElement>("/api/orders?pageSize=100", Ct);
        Assert.Equal(4800, orders.GetProperty("total").GetInt32());
        using var overview = await _client.GetAsync("/api/analytics/categories?range=12m", Ct);
        Assert.Equal(HttpStatusCode.OK, overview.StatusCode);
    }
}
