using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Nebula.Api.OpenApi;

/// <summary>Document-level polish: API description, tag order with descriptions, and the <c>/health</c> probe.</summary>
public sealed class NebulaDocumentTransformer : IOpenApiDocumentTransformer
{
    public const string HealthTag = "Health";

    /// <summary>Tags in the order Swagger UI shows them.</summary>
    public static readonly IReadOnlyList<(string Name, string Description)> Tags =
    [
        ("Auth", "Demo sign-in. Returns the JWT used by every other endpoint."),
        ("Orders", "Order list, details and status workflow (new → packing → shipped → delivered, or cancelled)."),
        ("Products", "Product catalog CRUD with validation."),
        ("Customers", "Customers with lifetime value and their order history."),
        ("Analytics", "Dashboard aggregates over a rolling range: KPIs, revenue, categories, heatmap, geo, funnel, top products."),
        ("Live", "Simulated incoming orders for the live dashboard feed."),
        ("Demo", "Demo data controls."),
        (HealthTag, "Liveness and database readiness probe (anonymous, not rate limited)."),
    ];

    private const string Description =
        """
        REST backend for the **Nebula Commerce** admin dashboard (Angular). It implements exactly the contract the app's
        in-browser mock serves, so the UI can switch between the mock and this API at runtime.

        ### Try it
        1. `POST /api/auth/login` with `alex@nebula.store` / `demo1234` — any email with a password of at least
           6 characters works.
        2. Press **Authorize** and paste the `token` from the response.

        ### Conventions
        - JSON in camelCase; timestamps are ISO 8601 UTC (`2026-09-24T12:00:00Z`); money has 2 decimals.
        - Lists take `page`, `pageSize` (1..100, default 20), `sort`, `dir` (`asc|desc`) and `search`, and return
          `{ items, total, page, pageSize }`. Invalid paging values fall back to the defaults.
        - Requests are rate limited per client IP; excess requests get `429` with `Retry-After`.

        ### Errors
        Every error is `application/problem+json`: standard problem details (`type`, `title`, `status`, `traceId`)
        plus `code`, `message` and — for `422 validation` — `details` mapping each invalid field to its first message:

        ```json
        { "status": 422, "code": "validation", "message": "Product is invalid",
          "details": { "price": "Price must be greater than 0" } }
        ```

        ### Demo data
        The SQLite database is generated from a deterministic seed and re-seeded every 6 hours
        (or on demand with `POST /api/demo/reset`), so feel free to change anything.
        """;

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.Info.Title = "Nebula Commerce API";
        document.Info.Version = OpenApiSetup.DocumentName;
        document.Info.Description = Description;

        // Insertion order is the display order.
        document.Tags = new HashSet<OpenApiTag>(Tags.Select(t => new OpenApiTag { Name = t.Name, Description = t.Description }));

        AddHealth(document);
        return Task.CompletedTask;
    }

    /// <summary><c>MapHealthChecks</c> endpoints are invisible to API Explorer, so describe the probe by hand.</summary>
    private static void AddHealth(OpenApiDocument document)
    {
        var body = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Required = new HashSet<string> { "status" },
            Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["status"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    Enum = ["Healthy", "Degraded", "Unhealthy"],
                },
            },
        };

        OpenApiResponse Response(string description) => new()
        {
            Description = description,
            Content = new Dictionary<string, OpenApiMediaType> { ["application/json"] = new() { Schema = body } },
        };

        var operation = new OpenApiOperation
        {
            OperationId = "GetHealth",
            Summary = "Health check",
            Description = "Anonymous and not rate limited. Reports `Healthy` when the database answers.",
            Tags = new HashSet<OpenApiTagReference> { new(HealthTag, document) },
            Responses = new OpenApiResponses
            {
                ["200"] = Response("The API and its database are up."),
                ["503"] = Response("The database is unavailable."),
            },
        };

        document.Paths ??= [];
        document.Paths["/health"] = new OpenApiPathItem
        {
            Operations = new Dictionary<HttpMethod, OpenApiOperation> { [HttpMethod.Get] = operation },
        };
    }
}
