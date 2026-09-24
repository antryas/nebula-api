using System.Net;
using System.Text.Json;
using Nebula.IntegrationTests.Infrastructure;

namespace Nebula.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class CustomersEndpointsTests(NebulaApiFactory factory)
{
    private async Task<JsonElement> GetJsonAsync(string url)
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        using var response = await client.GetAsync(url, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync();
    }

    [Fact]
    public async Task Default_list_is_newest_customers_first()
    {
        var page = await GetJsonAsync("/api/customers");

        Assert.Equal(700, page.GetProperty("total").GetInt32());
        var items = page.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(20, items.Count);
        var created = items.Select(i => i.GetProperty("createdAt").GetDateTime()).ToList();
        Assert.Equal(created.OrderDescending(), created);

        var first = items[0];
        foreach (var key in new[] { "id", "name", "email", "avatarUrl", "phone", "country", "countryCode", "createdAt", "ordersCount", "lifetimeValue", "lastOrderAt", "notes" })
        {
            Assert.True(first.TryGetProperty(key, out _), $"missing {key}");
        }
    }

    [Fact]
    public async Task Sorts_by_lifetime_value()
    {
        var page = await GetJsonAsync("/api/customers?sort=lifetimeValue&dir=desc&pageSize=100");

        var values = page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("lifetimeValue").GetDecimal()).ToList();
        Assert.Equal(values.OrderDescending(), values);
        Assert.True(values[0] > 0);
    }

    [Fact]
    public async Task Search_matches_name_email_or_country()
    {
        var customer = await GetJsonAsync("/api/customers/cus_0001");
        var email = customer.GetProperty("customer").GetProperty("email").GetString()!;

        var page = await GetJsonAsync($"/api/customers?search={Uri.EscapeDataString(email.ToUpperInvariant())}");

        Assert.Contains(page.GetProperty("items").EnumerateArray(), i => i.GetProperty("id").GetString() == "cus_0001");
        Assert.All(
            page.GetProperty("items").EnumerateArray(),
            i => Assert.Contains(email, i.GetProperty("email").GetString()!, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Profile_lists_orders_newest_first()
    {
        var page = await GetJsonAsync("/api/customers?sort=ordersCount&dir=desc&pageSize=1");
        var id = page.GetProperty("items")[0].GetProperty("id").GetString();

        var profile = await GetJsonAsync($"/api/customers/{id}");

        Assert.Equal(id, profile.GetProperty("customer").GetProperty("id").GetString());
        var orders = profile.GetProperty("orders").EnumerateArray().ToList();
        Assert.True(orders.Count >= 2);
        Assert.All(orders, o => Assert.Equal(id, o.GetProperty("customerId").GetString()));
        var created = orders.Select(o => o.GetProperty("createdAt").GetDateTime()).ToList();
        Assert.Equal(created.OrderDescending(), created);
    }

    [Fact]
    public async Task Unknown_customer_is_404()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();

        using var response = await client.GetAsync("/api/customers/cus_9999", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.ReadJsonAsync();
        Assert.Equal("not_found", body.GetProperty("code").GetString());
        Assert.Equal("Customer not found", body.GetProperty("message").GetString());
    }
}
