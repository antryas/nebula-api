using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Nebula.Application.Common;
using Nebula.IntegrationTests.Infrastructure;

namespace Nebula.IntegrationTests;

/// <summary>The public demo's configuration: <c>Demo:ReadOnly</c> on.</summary>
public sealed class ReadOnlyApiFactory : NebulaApiFactory
{
    protected override IDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        ["Demo:ReadOnly"] = "true",
    };
}

/// <summary>
/// Visitor writes answer exactly like real ones (status, body, validation, business rules) with the dry-run header,
/// but a later read shows the seed unchanged.
/// </summary>
public sealed class ReadOnlyDemoTests(ReadOnlyApiFactory factory) : IClassFixture<ReadOnlyApiFactory>, IAsyncLifetime
{
    private const string Header = "X-Nebula-Dry-Run";

    private HttpClient _client = null!;

    public async ValueTask InitializeAsync() => _client = await factory.CreateAuthenticatedClientAsync();

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Dictionary<string, object?> ProductBody(string sku) => new()
    {
        ["sku"] = sku,
        ["name"] = "Dry Run Parka",
        ["description"] = "Never saved.",
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

    private async Task<JsonElement> GetJsonAsync(string url)
    {
        using var response = await _client.GetAsync(url, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains(Header));
        return await response.ReadJsonAsync();
    }

    private async Task<string> FirstOrderIdAsync(string status) =>
        (await GetJsonAsync($"/api/orders?status={status}&pageSize=1")).GetProperty("items")[0].GetProperty("id").GetString()!;

    private static void AssertDryRun(HttpResponseMessage response) =>
        Assert.Equal("true", Assert.Single(response.Headers.GetValues(Header)));

    private async Task ResetDirectlyAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IDemoResetter>().ResetAsync(Ct);
    }

    [Fact]
    public async Task Mode_reports_read_only()
    {
        var mode = await GetJsonAsync("/api/demo/mode");

        Assert.True(mode.GetProperty("readOnly").GetBoolean());
    }

    [Fact]
    public async Task Create_returns_the_real_201_but_saves_nothing()
    {
        using var first = await _client.PostAsJsonAsync("/api/products", ProductBody("DRY-PRK-001"), Ct);
        using var second = await _client.PostAsJsonAsync("/api/products", ProductBody("DRY-PRK-001"), Ct);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        AssertDryRun(first);
        var product = await first.ReadJsonAsync();
        // The id a real create would get; the rollback frees it (and the SKU) again.
        Assert.Equal("prd_000061", product.GetProperty("id").GetString());
        Assert.Equal("/api/products/prd_000061", first.Headers.Location?.OriginalString);
        Assert.Equal(7, product.GetProperty("stock").GetInt32());
        Assert.Equal("https://picsum.photos/seed/nebula-prd_000061/400/400", product.GetProperty("imageUrl").GetString());
        Assert.Equal(
            ["prd_000061_v1", "prd_000061_v2"],
            product.GetProperty("variants").EnumerateArray().Select(v => v.GetProperty("id").GetString()));
        Assert.Equal(NebulaApiFactory.Now, product.GetProperty("createdAt").GetDateTime().ToUniversalTime());

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(product.GetRawText(), (await second.ReadJsonAsync()).GetRawText());

        using var get = await _client.GetAsync("/api/products/prd_000061", Ct);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(60, (await GetJsonAsync("/api/products")).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Invalid_create_is_still_422_with_field_details()
    {
        var body = ProductBody("DRY-BAD-001");
        body["name"] = " ";
        body["price"] = 0;

        using var response = await _client.PostAsJsonAsync("/api/products", body, Ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        AssertDryRun(response);
        var problem = await response.ReadJsonAsync();
        Assert.Equal("validation", problem.GetProperty("code").GetString());
        Assert.Equal("Name is required", problem.GetProperty("details").GetProperty("name").GetString());
        Assert.Equal("Price must be greater than 0", problem.GetProperty("details").GetProperty("price").GetString());
    }

    [Fact]
    public async Task Duplicate_sku_is_still_422_on_sku()
    {
        var seeded = await GetJsonAsync("/api/products/prd_0002");

        using var response = await _client.PostAsJsonAsync(
            "/api/products", ProductBody(seeded.GetProperty("sku").GetString()!), Ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        AssertDryRun(response);
        Assert.Equal(
            "SKU is already in use",
            (await response.ReadJsonAsync()).GetProperty("details").GetProperty("sku").GetString());
    }

    [Fact]
    public async Task Update_returns_the_updated_product_but_saves_nothing()
    {
        var before = await GetJsonAsync("/api/products/prd_0003");
        var body = ProductBody(before.GetProperty("sku").GetString()!);
        body["name"] = "Offensive name nobody else should see";
        body["variants"] = Array.Empty<object>();
        body["stock"] = 5;

        using var response = await _client.PutAsJsonAsync("/api/products/prd_0003", body, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertDryRun(response);
        var updated = await response.ReadJsonAsync();
        Assert.Equal("prd_0003", updated.GetProperty("id").GetString());
        Assert.Equal("Offensive name nobody else should see", updated.GetProperty("name").GetString());
        Assert.Equal(5, updated.GetProperty("stock").GetInt32());
        Assert.Equal(before.GetProperty("createdAt").GetString(), updated.GetProperty("createdAt").GetString());

        Assert.Equal(before.GetRawText(), (await GetJsonAsync("/api/products/prd_0003")).GetRawText());
    }

    [Fact]
    public async Task Update_of_unknown_product_is_still_404()
    {
        using var response = await _client.PutAsJsonAsync("/api/products/prd_9999", ProductBody("DRY-404-001"), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertDryRun(response);
        Assert.Equal("Product not found", (await response.ReadJsonAsync()).GetProperty("message").GetString());
    }

    [Fact]
    public async Task Delete_returns_204_but_the_product_stays()
    {
        using var response = await _client.DeleteAsync("/api/products/prd_0004", Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        AssertDryRun(response);
        Assert.Equal("prd_0004", (await GetJsonAsync("/api/products/prd_0004")).GetProperty("id").GetString());
    }

    [Fact]
    public async Task Status_change_returns_the_updated_order_but_saves_nothing()
    {
        var id = await FirstOrderIdAsync("new");
        var before = await GetJsonAsync($"/api/orders/{id}");

        using var response = await _client.PatchAsJsonAsync($"/api/orders/{id}/status", new { status = "packing" }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertDryRun(response);
        var order = await response.ReadJsonAsync();
        Assert.Equal("packing", order.GetProperty("status").GetString());
        var history = order.GetProperty("history");
        Assert.Equal(before.GetProperty("history").GetArrayLength() + 1, history.GetArrayLength());
        Assert.Equal("2026-09-24T12:00:00Z", history.EnumerateArray().Last().GetProperty("at").GetString());

        Assert.Equal(before.GetRawText(), (await GetJsonAsync($"/api/orders/{id}")).GetRawText());
    }

    [Fact]
    public async Task Cancellation_also_rolls_back_sold_units_and_customer_aggregates()
    {
        var id = await FirstOrderIdAsync("packing");
        var order = await GetJsonAsync($"/api/orders/{id}");
        var productId = order.GetProperty("items")[0].GetProperty("productId").GetString()!;
        var customerId = order.GetProperty("customerId").GetString()!;
        var product = await GetJsonAsync($"/api/products/{productId}");
        var customer = await GetJsonAsync($"/api/customers/{customerId}");

        using var response = await _client.PatchAsJsonAsync($"/api/orders/{id}/status", new { status = "cancelled" }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertDryRun(response);
        Assert.Equal("cancelled", (await response.ReadJsonAsync()).GetProperty("status").GetString());
        Assert.Equal(order.GetRawText(), (await GetJsonAsync($"/api/orders/{id}")).GetRawText());
        Assert.Equal(product.GetRawText(), (await GetJsonAsync($"/api/products/{productId}")).GetRawText());
        Assert.Equal(customer.GetRawText(), (await GetJsonAsync($"/api/customers/{customerId}")).GetRawText());
    }

    [Fact]
    public async Task Status_change_of_closed_order_is_still_422_invalid_transition()
    {
        var id = await FirstOrderIdAsync("delivered");

        using var response = await _client.PatchAsJsonAsync($"/api/orders/{id}/status", new { status = "shipped" }, Ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        AssertDryRun(response);
        Assert.Equal("invalid_transition", (await response.ReadJsonAsync()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Bulk_status_counts_the_updates_but_saves_nothing()
    {
        var open = (await GetJsonAsync("/api/orders?status=packing&pageSize=2")).GetProperty("items")
            .EnumerateArray().Select(i => i.GetProperty("id").GetString()!).ToList();

        using var response = await _client.PostAsJsonAsync("/api/orders/bulk-status", new { ids = open, status = "shipped" }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertDryRun(response);
        Assert.Equal(2, (await response.ReadJsonAsync()).GetProperty("updated").GetInt32());
        foreach (var id in open)
        {
            Assert.Equal("packing", (await GetJsonAsync($"/api/orders/{id}")).GetProperty("status").GetString());
        }

        using var invalid = await _client.PostAsJsonAsync("/api/orders/bulk-status", new { status = "shipped" }, Ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
        AssertDryRun(invalid);
    }

    [Fact]
    public async Task Live_orders_persist_and_reset_is_a_no_op()
    {
        using var tick = await _client.PostAsync("/api/live/tick", null, Ct);
        Assert.Equal(HttpStatusCode.Created, tick.StatusCode);
        Assert.False(tick.Headers.Contains(Header));
        var id = (await tick.ReadJsonAsync()).GetProperty("id").GetString();

        using var reset = await _client.PostAsync("/api/demo/reset", null, Ct);

        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        AssertDryRun(reset);
        Assert.Equal(id, (await GetJsonAsync($"/api/orders/{id}")).GetProperty("id").GetString());

        // The trusted periodic reset still re-seeds.
        await ResetDirectlyAsync();
        using var gone = await _client.GetAsync($"/api/orders/{id}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task Dry_runs_racing_live_orders_and_a_periodic_reset_all_succeed()
    {
        var creates = Enumerable.Range(0, 6)
            .Select(_ => _client.PostAsJsonAsync("/api/products", ProductBody("DRY-RACE-001"), Ct))
            .ToList();
        var ticks = Enumerable.Range(0, 6).Select(_ => _client.PostAsync("/api/live/tick", null, Ct)).ToList();
        var reset = Task.Run(ResetDirectlyAsync, Ct);

        var responses = await Task.WhenAll(creates.Concat(ticks));
        try
        {
            await reset;
            Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
            foreach (var response in responses.Take(creates.Count))
            {
                Assert.Equal("prd_000061", (await response.ReadJsonAsync()).GetProperty("id").GetString());
            }
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }

            await ResetDirectlyAsync();
        }

        Assert.Equal(60, (await GetJsonAsync("/api/products")).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Dry_run_header_is_exposed_to_the_dashboard()
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/products/prd_0005");
        request.Headers.Add("Origin", "http://localhost:4410");

        using var response = await _client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        AssertDryRun(response);
        Assert.Contains(Header, response.Headers.GetValues("Access-Control-Expose-Headers"), StringComparer.OrdinalIgnoreCase);
    }
}
