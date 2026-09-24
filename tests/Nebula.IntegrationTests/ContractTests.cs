using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Nebula.IntegrationTests.Infrastructure;

namespace Nebula.IntegrationTests;

/// <summary>
/// Locks the JSON wire format to the Angular models. The property lists are copied verbatim from
/// <c>nebula-commerce/src/app/models/*.ts</c> and <c>src/app/core/api/*.ts</c>; if a test here fails,
/// either the backend drifted or the frontend contract changed and both sides must be updated together.
/// Read-only: nothing here changes the shared database.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed partial class ContractTests(NebulaApiFactory factory) : IAsyncLifetime
{
    // --- TS interfaces (property names exactly as declared, optional ones listed separately) ---

    private static readonly string[] PagedProps = ["items", "total", "page", "pageSize"];

    private static readonly string[] OrderProps =
    [
        "id", "number", "customerId", "customerName", "customerEmail", "customerAvatarUrl", "items", "subtotal",
        "shipping", "tax", "total", "status", "paymentMethod", "createdAt", "shippingAddress", "history",
    ];

    private static readonly string[] OrderItemProps = ["productId", "name", "imageUrl", "sku", "quantity", "unitPrice"];
    private static readonly string[] AddressProps = ["line1", "city", "country", "countryCode", "postalCode"];
    private static readonly string[] StatusChangeProps = ["status", "at"];
    private static readonly string[] StatusChangeOptionalProps = ["note"];

    private static readonly string[] ProductProps =
    [
        "id", "sku", "name", "description", "category", "price", "compareAtPrice", "imageUrl", "stock", "sold",
        "rating", "variants", "createdAt", "active",
    ];

    private static readonly string[] ProductVariantProps = ["id", "size", "color", "stock"];

    private static readonly string[] CustomerProps =
    [
        "id", "name", "email", "avatarUrl", "phone", "country", "countryCode", "createdAt", "ordersCount",
        "lifetimeValue", "lastOrderAt", "notes",
    ];

    private static readonly string[] CustomerProfileProps = ["customer", "orders"];
    private static readonly string[] LoginResponseProps = ["token", "user"];
    private static readonly string[] UserProps = ["id", "name", "email", "avatarUrl", "role"];
    private static readonly string[] KpiProps = ["key", "label", "value", "previous", "deltaPct", "spark", "format"];
    private static readonly string[] TimePointProps = ["date", "revenue", "orders"];
    private static readonly string[] CategorySalesProps = ["category", "revenue"];
    private static readonly string[] HeatCellProps = ["weekday", "hour", "orders"];
    private static readonly string[] GeoSalesProps = ["countryCode", "country", "revenue", "orders"];
    private static readonly string[] FunnelStepProps = ["step", "value"];
    private static readonly string[] TopProductProps = ["product", "unitsSold", "revenue"];
    private static readonly string[] BulkStatusResultProps = ["updated"];

    /// <summary>TS <c>ApiError</c> fields; the RFC 7807 members around them are allowed extras.</summary>
    private static readonly string[] ApiErrorProps = ["status", "code", "message"];
    private static readonly string[] ApiErrorOptionalProps = ["details", "type", "title", "detail", "traceId"];

    // --- TS union types ---

    private static readonly string[] OrderStatuses = ["new", "packing", "shipped", "delivered", "cancelled"];
    private static readonly string[] PaymentMethods = ["card", "paypal", "apple_pay"];
    private static readonly string[] Categories = ["Apparel", "Footwear", "Accessories", "Electronics", "Home", "Beauty"];
    private static readonly string[] KpiKeys = ["revenue", "orders", "aov", "conversion"];
    private static readonly string[] KpiFormats = ["currency", "number", "percent"];
    private static readonly string[] FunnelSteps = ["Visits", "Product views", "Added to cart", "Checkout", "Paid"];

    private HttpClient _client = null!;

    public async ValueTask InitializeAsync() => _client = await factory.CreateAuthenticatedClientAsync();

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Paged_orders_match_Order_OrderItem_Address_and_StatusChange()
    {
        var page = await GetJsonAsync("/api/orders?pageSize=100");

        AssertPaged(page, 100);
        var orders = page.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(100, orders.Count);
        foreach (var order in orders)
        {
            AssertOrder(order);
        }

        // The seed spreads over every status and payment method, so the union checks above saw them all.
        Assert.Equal(
            OrderStatuses.Order(StringComparer.Ordinal),
            orders.Select(o => o.GetProperty("status").GetString()!).Distinct().Order(StringComparer.Ordinal));
        Assert.Equal(
            PaymentMethods.Order(StringComparer.Ordinal),
            orders.Select(o => o.GetProperty("paymentMethod").GetString()!).Distinct().Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Single_order_matches_Order()
    {
        AssertOrder(await GetJsonAsync("/api/orders/ord_000001"));
    }

    [Fact]
    public async Task Paged_products_match_Product_and_ProductVariant()
    {
        var page = await GetJsonAsync("/api/products?pageSize=100");

        AssertPaged(page, 100);
        var products = page.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(60, products.Count);
        foreach (var product in products)
        {
            AssertProduct(product);
        }

        var compareAt = products.Select(p => p.GetProperty("compareAtPrice").ValueKind).ToHashSet();
        Assert.Contains(JsonValueKind.Null, compareAt);
        Assert.Contains(JsonValueKind.Number, compareAt);
        Assert.Contains(JsonValueKind.False, products.Select(p => p.GetProperty("active").ValueKind));
    }

    [Fact]
    public async Task Single_product_matches_Product()
    {
        AssertProduct(await GetJsonAsync("/api/products/prd_0001"));
    }

    [Fact]
    public async Task Paged_customers_match_Customer()
    {
        // Both ends of the lastOrderAt sort, so customers with and without orders are covered.
        List<JsonElement> customers = [];
        foreach (var dir in new[] { "asc", "desc" })
        {
            var page = await GetJsonAsync($"/api/customers?pageSize=100&sort=lastOrderAt&dir={dir}");
            AssertPaged(page, 100);
            customers.AddRange(page.GetProperty("items").EnumerateArray());
        }

        foreach (var customer in customers)
        {
            AssertCustomer(customer);
        }

        var lastOrderKinds = customers.Select(c => c.GetProperty("lastOrderAt").ValueKind).ToHashSet();
        Assert.Contains(JsonValueKind.Null, lastOrderKinds);
        Assert.Contains(JsonValueKind.String, lastOrderKinds);
    }

    [Fact]
    public async Task Customer_profile_matches_CustomerProfile()
    {
        var profile = await GetJsonAsync("/api/customers/cus_0001");

        AssertProps(profile, CustomerProfileProps);
        AssertCustomer(profile.GetProperty("customer"));
        var orders = profile.GetProperty("orders").EnumerateArray().ToList();
        Assert.NotEmpty(orders);
        foreach (var order in orders)
        {
            AssertOrder(order);
            Assert.Equal("cus_0001", order.GetProperty("customerId").GetString());
        }
    }

    [Fact]
    public async Task Login_matches_LoginResponse_and_User()
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = ApiClientExtensions.DemoEmail, password = ApiClientExtensions.DemoPassword },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var login = await response.ReadJsonAsync();

        AssertProps(login, LoginResponseProps);
        Assert.Equal(3, AssertString(login, "token").Split('.').Length);
        var user = login.GetProperty("user");
        AssertProps(user, UserProps);
        Assert.Equal("usr_1", AssertString(user, "id"));
        AssertString(user, "name");
        AssertString(user, "email");
        AssertUrl(user, "avatarUrl");
        Assert.Equal("Admin", AssertString(user, "role"));
    }

    [Fact]
    public async Task Bulk_status_matches_its_response_type()
    {
        // Unknown ids are skipped, so this leaves the shared database untouched.
        using var response = await _client.PostAsJsonAsync(
            "/api/orders/bulk-status",
            new { ids = new[] { "ord_999999" }, status = "shipped" },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.ReadJsonAsync();

        AssertProps(result, BulkStatusResultProps);
        Assert.Equal(0, AssertInt(result, "updated"));
    }

    [Fact]
    public async Task Overview_matches_Kpi()
    {
        var kpis = await GetArrayAsync("/api/analytics/overview?range=30d");

        Assert.Equal(KpiKeys, kpis.Select(k => k.GetProperty("key").GetString()));
        foreach (var kpi in kpis)
        {
            AssertProps(kpi, KpiProps);
            AssertOneOf(kpi, "key", KpiKeys);
            AssertString(kpi, "label");
            AssertMoney(kpi, "value");
            AssertMoney(kpi, "previous");
            AssertMoney(kpi, "deltaPct");
            var spark = kpi.GetProperty("spark");
            Assert.Equal(JsonValueKind.Array, spark.ValueKind);
            Assert.Equal(12, spark.GetArrayLength());
            Assert.All(spark.EnumerateArray(), s => Assert.Equal(JsonValueKind.Number, s.ValueKind));
            AssertOneOf(kpi, "format", KpiFormats);
        }
    }

    [Theory]
    [InlineData("7d", "^\\d{4}-\\d{2}-\\d{2}$")]
    [InlineData("12m", "^\\d{4}-\\d{2}-01$")]
    public async Task Revenue_matches_TimePoint(string range, string datePattern)
    {
        var points = await GetArrayAsync($"/api/analytics/revenue?range={range}");

        Assert.NotEmpty(points);
        foreach (var point in points)
        {
            AssertProps(point, TimePointProps);
            Assert.Matches(datePattern, AssertString(point, "date"));
            AssertMoney(point, "revenue");
            AssertInt(point, "orders");
        }
    }

    [Fact]
    public async Task Categories_match_CategorySales()
    {
        var rows = await GetArrayAsync("/api/analytics/categories");

        Assert.NotEmpty(rows);
        foreach (var row in rows)
        {
            AssertProps(row, CategorySalesProps);
            AssertOneOf(row, "category", Categories);
            AssertMoney(row, "revenue");
        }
    }

    [Fact]
    public async Task Heatmap_matches_HeatCell()
    {
        var cells = await GetArrayAsync("/api/analytics/heatmap");

        Assert.Equal(7 * 24, cells.Count);
        foreach (var cell in cells)
        {
            AssertProps(cell, HeatCellProps);
            Assert.InRange(AssertInt(cell, "weekday"), 0, 6);
            Assert.InRange(AssertInt(cell, "hour"), 0, 23);
            Assert.True(AssertInt(cell, "orders") >= 0);
        }
    }

    [Fact]
    public async Task Geo_matches_GeoSales()
    {
        var rows = await GetArrayAsync("/api/analytics/geo");

        Assert.NotEmpty(rows);
        foreach (var row in rows)
        {
            AssertProps(row, GeoSalesProps);
            Assert.Matches("^[A-Z]{2}$", AssertString(row, "countryCode"));
            AssertString(row, "country");
            AssertMoney(row, "revenue");
            AssertInt(row, "orders");
        }
    }

    [Fact]
    public async Task Funnel_matches_FunnelStep()
    {
        var steps = await GetArrayAsync("/api/analytics/funnel");

        Assert.Equal(FunnelSteps, steps.Select(s => s.GetProperty("step").GetString()));
        foreach (var step in steps)
        {
            AssertProps(step, FunnelStepProps);
            AssertInt(step, "value");
        }
    }

    [Fact]
    public async Task Top_products_match_TopProduct()
    {
        var rows = await GetArrayAsync("/api/analytics/top-products?range=90d&limit=10");

        Assert.Equal(10, rows.Count);
        foreach (var row in rows)
        {
            AssertProps(row, TopProductProps);
            AssertProduct(row.GetProperty("product"));
            AssertInt(row, "unitsSold");
            AssertMoney(row, "revenue");
        }
    }

    [Fact]
    public async Task Not_found_matches_ApiError_without_details()
    {
        using var response = await _client.GetAsync("/api/orders/ord_999999", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var error = await AssertApiErrorAsync(response, 404, "not_found");
        Assert.False(error.TryGetProperty("details", out _));
    }

    [Fact]
    public async Task Validation_error_matches_ApiError_with_field_details()
    {
        using var response = await _client.PostAsJsonAsync("/api/products", new { }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var error = await AssertApiErrorAsync(response, 422, "validation");
        var details = error.GetProperty("details");
        Assert.Equal(JsonValueKind.Object, details.ValueKind);
        Assert.NotEmpty(details.EnumerateObject());
        Assert.All(details.EnumerateObject(), d =>
        {
            Assert.Matches("^[a-z][A-Za-z0-9.\\[\\]]*$", d.Name);
            Assert.Equal(JsonValueKind.String, d.Value.ValueKind);
        });
    }

    // --- Composite shapes ---

    private static void AssertPaged(JsonElement page, int pageSize)
    {
        AssertProps(page, PagedProps);
        Assert.Equal(JsonValueKind.Array, page.GetProperty("items").ValueKind);
        Assert.True(AssertInt(page, "total") >= 0);
        Assert.Equal(1, AssertInt(page, "page"));
        Assert.Equal(pageSize, AssertInt(page, "pageSize"));
    }

    private static void AssertOrder(JsonElement order)
    {
        AssertProps(order, OrderProps);
        Assert.Matches(OrderId(), AssertString(order, "id"));
        Assert.True(AssertInt(order, "number") >= 1001);
        Assert.Matches(CustomerId(), AssertString(order, "customerId"));
        AssertString(order, "customerName");
        AssertString(order, "customerEmail");
        AssertUrl(order, "customerAvatarUrl");

        var items = order.GetProperty("items");
        Assert.Equal(JsonValueKind.Array, items.ValueKind);
        Assert.InRange(items.GetArrayLength(), 1, 10);
        foreach (var item in items.EnumerateArray())
        {
            AssertProps(item, OrderItemProps);
            Assert.Matches(ProductId(), AssertString(item, "productId"));
            AssertString(item, "name");
            AssertUrl(item, "imageUrl");
            AssertString(item, "sku");
            Assert.True(AssertInt(item, "quantity") >= 1);
            AssertMoney(item, "unitPrice");
        }

        AssertMoney(order, "subtotal");
        AssertMoney(order, "shipping");
        AssertMoney(order, "tax");
        AssertMoney(order, "total");
        AssertOneOf(order, "status", OrderStatuses);
        AssertOneOf(order, "paymentMethod", PaymentMethods);
        AssertTimestamp(order, "createdAt");

        var address = order.GetProperty("shippingAddress");
        AssertProps(address, AddressProps);
        AssertString(address, "line1");
        AssertString(address, "city");
        AssertString(address, "country");
        Assert.Matches("^[A-Z]{2}$", AssertString(address, "countryCode"));
        AssertString(address, "postalCode");

        var history = order.GetProperty("history");
        Assert.Equal(JsonValueKind.Array, history.ValueKind);
        Assert.NotEmpty(history.EnumerateArray());
        foreach (var change in history.EnumerateArray())
        {
            AssertProps(change, StatusChangeProps, StatusChangeOptionalProps);
            AssertOneOf(change, "status", OrderStatuses);
            AssertTimestamp(change, "at");
            if (change.TryGetProperty("note", out var note))
            {
                Assert.Equal(JsonValueKind.String, note.ValueKind);
            }
        }
    }

    private static void AssertProduct(JsonElement product)
    {
        AssertProps(product, ProductProps);
        var id = AssertString(product, "id");
        Assert.Matches(ProductId(), id);
        AssertString(product, "sku");
        AssertString(product, "name");
        AssertString(product, "description");
        AssertOneOf(product, "category", Categories);
        AssertMoney(product, "price");
        var compareAt = product.GetProperty("compareAtPrice");
        Assert.True(compareAt.ValueKind is JsonValueKind.Number or JsonValueKind.Null, $"compareAtPrice is {compareAt.ValueKind}");
        if (compareAt.ValueKind == JsonValueKind.Number)
        {
            AssertMoney(product, "compareAtPrice");
        }

        AssertUrl(product, "imageUrl");
        Assert.True(AssertInt(product, "stock") >= 0);
        Assert.True(AssertInt(product, "sold") >= 0);
        Assert.InRange(AssertNumber(product, "rating"), 0, 5);
        AssertTimestamp(product, "createdAt");
        var active = product.GetProperty("active").ValueKind;
        Assert.True(active is JsonValueKind.True or JsonValueKind.False, $"active is {active}");

        var variants = product.GetProperty("variants");
        Assert.Equal(JsonValueKind.Array, variants.ValueKind);
        foreach (var variant in variants.EnumerateArray())
        {
            AssertProps(variant, ProductVariantProps);
            Assert.StartsWith(id + "_v", AssertString(variant, "id"), StringComparison.Ordinal);
            AssertString(variant, "size");
            AssertString(variant, "color");
            Assert.True(AssertInt(variant, "stock") >= 0);
        }
    }

    private static void AssertCustomer(JsonElement customer)
    {
        AssertProps(customer, CustomerProps);
        Assert.Matches(CustomerId(), AssertString(customer, "id"));
        AssertString(customer, "name");
        Assert.Contains('@', AssertString(customer, "email"));
        AssertUrl(customer, "avatarUrl");
        AssertString(customer, "phone");
        AssertString(customer, "country");
        Assert.Matches("^[A-Z]{2}$", AssertString(customer, "countryCode"));
        AssertTimestamp(customer, "createdAt");
        Assert.True(AssertInt(customer, "ordersCount") >= 0);
        AssertMoney(customer, "lifetimeValue");
        var lastOrderAt = customer.GetProperty("lastOrderAt");
        Assert.True(lastOrderAt.ValueKind is JsonValueKind.String or JsonValueKind.Null, $"lastOrderAt is {lastOrderAt.ValueKind}");
        if (lastOrderAt.ValueKind == JsonValueKind.String)
        {
            AssertTimestamp(customer, "lastOrderAt");
        }

        Assert.Equal(JsonValueKind.String, customer.GetProperty("notes").ValueKind);
    }

    private static async Task<JsonElement> AssertApiErrorAsync(HttpResponseMessage response, int status, string code)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var error = await response.ReadJsonAsync();
        AssertProps(error, ApiErrorProps, ApiErrorOptionalProps);
        Assert.Equal(status, AssertInt(error, "status"));
        Assert.Equal(code, AssertString(error, "code"));
        AssertString(error, "message");
        AssertString(error, "traceId");
        return error;
    }

    // --- Primitive checks ---

    /// <summary>Exact property-name set: every required name, optional names allowed, nothing else.</summary>
    private static void AssertProps(JsonElement element, string[] required, string[]? optional = null)
    {
        Assert.Equal(JsonValueKind.Object, element.ValueKind);
        var actual = element.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var missing = required.Where(r => !actual.Contains(r)).ToList();
        var unexpected = actual.Except(required).Except(optional ?? []).Order(StringComparer.Ordinal).ToList();
        Assert.True(missing.Count == 0, $"Missing properties: {string.Join(", ", missing)}");
        Assert.True(unexpected.Count == 0, $"Unexpected properties: {string.Join(", ", unexpected)}");
    }

    private static string AssertString(JsonElement element, string name)
    {
        var value = element.GetProperty(name);
        Assert.True(value.ValueKind == JsonValueKind.String, $"{name} is {value.ValueKind}, expected String");
        var text = value.GetString()!;
        Assert.False(string.IsNullOrWhiteSpace(text), $"{name} is blank");
        return text;
    }

    private static double AssertNumber(JsonElement element, string name)
    {
        var value = element.GetProperty(name);
        Assert.True(value.ValueKind == JsonValueKind.Number, $"{name} is {value.ValueKind}, expected Number");
        return value.GetDouble();
    }

    private static int AssertInt(JsonElement element, string name)
    {
        AssertNumber(element, name);
        Assert.True(element.GetProperty(name).TryGetInt32(out var value), $"{name} is not an integer");
        return value;
    }

    /// <summary>A number with at most two decimals.</summary>
    private static void AssertMoney(JsonElement element, string name)
    {
        AssertNumber(element, name);
        var value = element.GetProperty(name).GetDecimal();
        Assert.True(decimal.Round(value, 2) == value, $"{name} = {value} has more than 2 decimals");
    }

    private static void AssertOneOf(JsonElement element, string name, string[] allowed) =>
        Assert.Contains(AssertString(element, name), allowed);

    /// <summary>ISO 8601 UTC with a <c>Z</c> suffix, as <c>Date.prototype.toISOString</c> writes it.</summary>
    private static void AssertTimestamp(JsonElement element, string name)
    {
        var text = AssertString(element, name);
        Assert.Matches(Timestamp(), text);
        Assert.True(
            DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            && parsed.Kind == DateTimeKind.Utc,
            $"{name} = {text} is not a UTC timestamp");
    }

    private static void AssertUrl(JsonElement element, string name) =>
        Assert.StartsWith("https://", AssertString(element, name), StringComparison.Ordinal);

    [System.Text.RegularExpressions.GeneratedRegex("^ord_\\d{6}$")]
    private static partial System.Text.RegularExpressions.Regex OrderId();

    [System.Text.RegularExpressions.GeneratedRegex("^cus_\\d{4}$")]
    private static partial System.Text.RegularExpressions.Regex CustomerId();

    /// <summary>Seeded products are <c>prd_0001</c>; products created later get six digits, like the mock.</summary>
    [System.Text.RegularExpressions.GeneratedRegex("^prd_(\\d{4}|\\d{6})$")]
    private static partial System.Text.RegularExpressions.Regex ProductId();

    [System.Text.RegularExpressions.GeneratedRegex("^\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}:\\d{2}(\\.\\d{1,7})?Z$")]
    private static partial System.Text.RegularExpressions.Regex Timestamp();

    private async Task<JsonElement> GetJsonAsync(string url)
    {
        using var response = await _client.GetAsync(url, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync();
    }

    private async Task<List<JsonElement>> GetArrayAsync(string url)
    {
        var json = await GetJsonAsync(url);
        Assert.Equal(JsonValueKind.Array, json.ValueKind);
        return [.. json.EnumerateArray()];
    }
}
