using System.Net;
using System.Text.Json;
using Nebula.IntegrationTests.Infrastructure;

namespace Nebula.IntegrationTests;

/// <summary>The published OpenAPI document is part of the showcase: complete, ordered and documented.</summary>
[Collection(ApiCollection.Name)]
public sealed class OpenApiDocumentTests(NebulaApiFactory factory)
{
    private static readonly string[] Operations =
    [
        "POST /api/auth/login",
        "GET /api/orders",
        "GET /api/orders/{id}",
        "PATCH /api/orders/{id}/status",
        "POST /api/orders/bulk-status",
        "GET /api/products",
        "GET /api/products/{id}",
        "POST /api/products",
        "PUT /api/products/{id}",
        "DELETE /api/products/{id}",
        "GET /api/customers",
        "GET /api/customers/{id}",
        "GET /api/analytics/overview",
        "GET /api/analytics/revenue",
        "GET /api/analytics/categories",
        "GET /api/analytics/heatmap",
        "GET /api/analytics/geo",
        "GET /api/analytics/funnel",
        "GET /api/analytics/top-products",
        "POST /api/live/tick",
        "POST /api/demo/reset",
        "GET /health",
    ];

    private static readonly string[] TagOrder = ["Auth", "Orders", "Products", "Customers", "Analytics", "Live", "Demo", "Health"];

    private async Task<JsonElement> GetDocumentAsync()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync();
    }

    private static IEnumerable<(string Key, JsonElement Operation)> EnumerateOperations(JsonElement document) =>
        from path in document.GetProperty("paths").EnumerateObject()
        from operation in path.Value.EnumerateObject()
        select ($"{operation.Name.ToUpperInvariant()} {path.Name}", operation.Value);

    [Fact]
    public async Task Document_lists_all_22_operations_with_summary_and_description()
    {
        var document = await GetDocumentAsync();

        var operations = EnumerateOperations(document).ToList();
        Assert.Equal(
            Operations.Order(StringComparer.Ordinal),
            operations.Select(o => o.Key).Order(StringComparer.Ordinal));
        Assert.All(operations, o =>
        {
            Assert.False(string.IsNullOrWhiteSpace(o.Operation.GetProperty("summary").GetString()), $"{o.Key} has no summary");
            Assert.False(string.IsNullOrWhiteSpace(o.Operation.GetProperty("description").GetString()), $"{o.Key} has no description");
            Assert.Single(o.Operation.GetProperty("tags").EnumerateArray());
            Assert.True(o.Operation.GetProperty("responses").EnumerateObject().Any(r => r.Name.StartsWith('2')), $"{o.Key} has no success response");
        });
    }

    [Fact]
    public async Task Tags_are_ordered_and_described()
    {
        var document = await GetDocumentAsync();

        var tags = document.GetProperty("tags").EnumerateArray().ToList();
        Assert.Equal(TagOrder, tags.Select(t => t.GetProperty("name").GetString()));
        Assert.All(tags, t => Assert.False(string.IsNullOrWhiteSpace(t.GetProperty("description").GetString())));
    }

    [Fact]
    public async Task Description_explains_demo_credentials_errors_and_reset()
    {
        var description = (await GetDocumentAsync()).GetProperty("info").GetProperty("description").GetString()!;

        Assert.Contains("alex@nebula.store", description, StringComparison.Ordinal);
        Assert.Contains("demo1234", description, StringComparison.Ordinal);
        Assert.Contains("application/problem+json", description, StringComparison.Ordinal);
        Assert.Contains("every 6 hours", description, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("OrderStatus", new[] { "new", "packing", "shipped", "delivered", "cancelled" })]
    [InlineData("PaymentMethod", new[] { "card", "paypal", "apple_pay" })]
    [InlineData("ProductCategory", new[] { "Apparel", "Footwear", "Accessories", "Electronics", "Home", "Beauty" })]
    public async Task Enums_are_string_enums_with_wire_values(string name, string[] values)
    {
        var schema = (await GetDocumentAsync()).GetProperty("components").GetProperty("schemas").GetProperty(name);

        Assert.Equal("string", schema.GetProperty("type").GetString());
        Assert.Equal(values, schema.GetProperty("enum").EnumerateArray().Select(v => v.GetString()));
    }

    [Fact]
    public async Task Schemas_are_named_like_the_frontend_models_and_numbers_are_numbers()
    {
        var schemas = (await GetDocumentAsync()).GetProperty("components").GetProperty("schemas");

        Assert.True(schemas.TryGetProperty("Order", out var order));
        Assert.True(schemas.TryGetProperty("PagedOfOrder", out _));
        Assert.DoesNotContain(schemas.EnumerateObject(), s => s.Name.Contains("Dto", StringComparison.Ordinal));
        Assert.Equal("number", order.GetProperty("properties").GetProperty("total").GetProperty("type").GetString());
        Assert.Equal("integer", order.GetProperty("properties").GetProperty("number").GetProperty("type").GetString());

        Assert.Equal(
            ["revenue", "orders", "aov", "conversion"],
            schemas.GetProperty("Kpi").GetProperty("properties").GetProperty("key").GetProperty("enum").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal(
            ["Admin"],
            schemas.GetProperty("User").GetProperty("properties").GetProperty("role").GetProperty("enum").EnumerateArray().Select(v => v.GetString()));

        var statusChange = schemas.GetProperty("StatusChange");
        Assert.DoesNotContain("note", statusChange.GetProperty("required").EnumerateArray().Select(r => r.GetString()));
    }

    [Fact]
    public async Task Problem_schema_documents_the_contract_fields()
    {
        var problem = (await GetDocumentAsync()).GetProperty("components").GetProperty("schemas").GetProperty("ProblemDetails");

        var properties = problem.GetProperty("properties");
        foreach (var name in new[] { "status", "code", "message", "details", "traceId" })
        {
            Assert.True(properties.TryGetProperty(name, out _), $"ProblemDetails lacks {name}");
        }
    }

    [Theory]
    [InlineData("/api/auth/login", "post", "email")]
    [InlineData("/api/products", "post", "sku")]
    public async Task Request_bodies_have_examples(string path, string method, string field)
    {
        var operation = (await GetDocumentAsync()).GetProperty("paths").GetProperty(path).GetProperty(method);

        var example = operation.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("example");
        Assert.True(example.TryGetProperty(field, out _));
    }
}
