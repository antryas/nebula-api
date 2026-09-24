using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Nebula.IntegrationTests.Infrastructure;

namespace Nebula.IntegrationTests;

/// <summary>Mutating tests: <see cref="SeedRestorer"/> restores the seed when the class is done, so the shared database stays pristine.</summary>
[Collection(ApiCollection.Name)]
public sealed class OrdersEndpointsTests(NebulaApiFactory factory) : IClassFixture<SeedRestorer>, IAsyncLifetime
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

    [Fact]
    public async Task Default_list_is_first_page_of_all_orders_newest_first()
    {
        var page = await GetJsonAsync("/api/orders");

        Assert.Equal(4800, page.GetProperty("total").GetInt32());
        Assert.Equal(1, page.GetProperty("page").GetInt32());
        Assert.Equal(20, page.GetProperty("pageSize").GetInt32());
        var items = page.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(20, items.Count);
        Assert.Equal(5800, items[0].GetProperty("number").GetInt32());
        var created = items.Select(i => i.GetProperty("createdAt").GetDateTime()).ToList();
        Assert.Equal(created.OrderDescending(), created);
    }

    [Fact]
    public async Task Filters_by_status_list()
    {
        var page = await GetJsonAsync("/api/orders?status=new,packing&pageSize=5");

        Assert.Equal(5, page.GetProperty("pageSize").GetInt32());
        var items = page.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(5, items.Count);
        Assert.All(items, i => Assert.Contains(i.GetProperty("status").GetString(), new[] { "new", "packing" }));
        Assert.True(page.GetProperty("total").GetInt32() < 4800);
    }

    [Fact]
    public async Task Filters_by_inclusive_date_range()
    {
        var newest = (await GetJsonAsync("/api/orders?pageSize=1")).GetProperty("items")[0];
        var at = newest.GetProperty("createdAt").GetString();

        var page = await GetJsonAsync($"/api/orders?from={Uri.EscapeDataString(at!)}&to={Uri.EscapeDataString(at!)}");

        Assert.Contains(
            page.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("id").GetString() == newest.GetProperty("id").GetString());
    }

    [Fact]
    public async Task Search_finds_order_by_number()
    {
        var page = await GetJsonAsync("/api/orders?search=1001");

        Assert.Contains(page.GetProperty("items").EnumerateArray(), i => i.GetProperty("number").GetInt32() == 1001);
    }

    [Fact]
    public async Task Sorts_by_total_ascending()
    {
        var page = await GetJsonAsync("/api/orders?sort=total&dir=asc&pageSize=50");

        var totals = page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("total").GetDecimal()).ToList();
        Assert.Equal(totals.Order(), totals);
    }

    [Fact]
    public async Task Get_by_id_returns_contract_shape()
    {
        var order = await GetJsonAsync("/api/orders/ord_000001");

        Assert.Equal("ord_000001", order.GetProperty("id").GetString());
        Assert.Equal(1001, order.GetProperty("number").GetInt32());
        Assert.EndsWith("Z", order.GetProperty("createdAt").GetString(), StringComparison.Ordinal);
        Assert.Contains(order.GetProperty("paymentMethod").GetString(), new[] { "card", "paypal", "apple_pay" });
        Assert.Equal(JsonValueKind.Array, order.GetProperty("items").ValueKind);
        Assert.Equal(JsonValueKind.Object, order.GetProperty("shippingAddress").ValueKind);
        Assert.True(order.GetProperty("shippingAddress").TryGetProperty("countryCode", out _));
        var history = order.GetProperty("history").EnumerateArray().ToList();
        Assert.Equal("new", history[0].GetProperty("status").GetString());
        Assert.Contains(history, h => !h.TryGetProperty("note", out _));
    }

    [Fact]
    public async Task Get_unknown_order_is_404()
    {
        using var response = await _client.GetAsync("/api/orders/ord_999999", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.ReadJsonAsync();
        Assert.Equal("not_found", body.GetProperty("code").GetString());
        Assert.Equal("Order not found", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Patch_status_appends_history()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = await FirstIdAsync("new");

        using var response = await _client.PatchAsJsonAsync($"/api/orders/{id}/status", new { status = "packing" }, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var order = await response.ReadJsonAsync();
        Assert.Equal("packing", order.GetProperty("status").GetString());
        var last = order.GetProperty("history").EnumerateArray().Last();
        Assert.Equal("packing", last.GetProperty("status").GetString());
        Assert.Equal("2026-09-24T12:00:00Z", last.GetProperty("at").GetString());
        Assert.False(last.TryGetProperty("note", out _));

        var reloaded = await GetJsonAsync($"/api/orders/{id}");
        Assert.Equal("packing", reloaded.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Patch_unknown_status_is_422_validation()
    {
        var id = await FirstIdAsync("new");

        using var response = await _client.PatchAsJsonAsync(
            $"/api/orders/{id}/status", new { status = "lost" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.ReadJsonAsync();
        Assert.Equal("validation", body.GetProperty("code").GetString());
        Assert.Equal("Unknown order status", body.GetProperty("message").GetString());
        Assert.Equal("Unknown order status", body.GetProperty("details").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Patch_closed_order_is_422_invalid_transition()
    {
        var page = await GetJsonAsync("/api/orders?status=delivered&pageSize=1");
        var closed = page.GetProperty("items")[0];

        using var response = await _client.PatchAsJsonAsync(
            $"/api/orders/{closed.GetProperty("id").GetString()}/status", new { status = "shipped" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.ReadJsonAsync();
        Assert.Equal("invalid_transition", body.GetProperty("code").GetString());
        Assert.Equal(
            $"Order #{closed.GetProperty("number").GetInt32()} is delivered and can no longer change status",
            body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Patch_unknown_order_is_404()
    {
        using var response = await _client.PatchAsJsonAsync(
            "/api/orders/ord_999999/status", new { status = "shipped" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Bulk_status_updates_open_orders_only()
    {
        var ct = TestContext.Current.CancellationToken;
        var open = (await GetJsonAsync("/api/orders?status=packing&pageSize=2")).GetProperty("items")
            .EnumerateArray().Select(i => i.GetProperty("id").GetString()!).ToList();
        var closed = await FirstIdAsync("cancelled");

        using var response = await _client.PostAsJsonAsync(
            "/api/orders/bulk-status", new { ids = open.Append(closed).Append("ord_999999"), status = "shipped" }, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.ReadJsonAsync();
        Assert.Equal(2, body.GetProperty("updated").GetInt32());
        foreach (var id in open)
        {
            Assert.Equal("shipped", (await GetJsonAsync($"/api/orders/{id}")).GetProperty("status").GetString());
        }

        Assert.Equal("cancelled", (await GetJsonAsync($"/api/orders/{closed}")).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Bulk_status_with_bad_body_is_422()
    {
        using var response = await _client.PostAsJsonAsync(
            "/api/orders/bulk-status", new { status = "shipped" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.ReadJsonAsync();
        Assert.Equal("validation", body.GetProperty("code").GetString());
        Assert.Equal("Expected { ids: string[]; status }", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task List_of_100_orders_is_fast()
    {
        await GetJsonAsync("/api/orders?pageSize=100"); // warm-up

        var stopwatch = Stopwatch.StartNew();
        var page = await GetJsonAsync("/api/orders?pageSize=100");
        stopwatch.Stop();

        Assert.Equal(100, page.GetProperty("items").GetArrayLength());
        // Budget is 300 ms locally; the bound here is generous so slow CI machines do not flake.
        Assert.True(stopwatch.ElapsedMilliseconds < 1000, $"took {stopwatch.ElapsedMilliseconds} ms");
    }

    [Theory]
    [InlineData("GET", "/api/orders")]
    [InlineData("GET", "/api/orders/ord_000001")]
    [InlineData("PATCH", "/api/orders/ord_000001/status")]
    [InlineData("POST", "/api/orders/bulk-status")]
    [InlineData("GET", "/api/customers")]
    [InlineData("GET", "/api/customers/cus_0001")]
    [InlineData("POST", "/api/live/tick")]
    public async Task Requires_a_token(string method, string url)
    {
        using var anonymous = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), url);

        using var response = await anonymous.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<string> FirstIdAsync(string status) =>
        (await GetJsonAsync($"/api/orders?status={status}&pageSize=1")).GetProperty("items")[0].GetProperty("id").GetString()!;
}
