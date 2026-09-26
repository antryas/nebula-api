using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Nebula.Application.Ai;
using Nebula.Application.Analytics;
using Nebula.Application.Auth;
using Nebula.Application.Orders;
using Nebula.Application.Products;
using Nebula.Domain;

namespace Nebula.Api.OpenApi;

/// <summary>
/// Makes the generated schemas describe the wire format the Angular app relies on instead of what the
/// lenient model binder would also accept.
/// </summary>
public sealed class ContractSchemaTransformer : IOpenApiSchemaTransformer
{
    /// <summary>Request properties the contract allows to be <c>null</c>; every other request property is non-null.</summary>
    private static readonly HashSet<(Type Type, string Property)> NullableRequestProperties =
    [
        (typeof(ProductInput), "compareAtPrice"),
    ];

    /// <summary>Request properties clients may leave out.</summary>
    private static readonly HashSet<(Type Type, string Property)> OptionalRequestProperties =
    [
        (typeof(AskRequest), "history"),
        (typeof(ProductDescriptionRequest), "keywords"),
    ];

    private static readonly HashSet<Type> RequestTypes =
    [
        typeof(LoginRequest), typeof(UpdateStatusRequest), typeof(BulkStatusRequest), typeof(ProductInput),
        typeof(ProductVariantInput), typeof(AskRequest), typeof(AiChatTurn), typeof(ProductDescriptionRequest),
    ];

    private static readonly string[] OrderStatusValues = ["new", "packing", "shipped", "delivered", "cancelled"];

    /// <summary>String properties that are TypeScript string-literal unions without a backing C# enum.</summary>
    private static readonly Dictionary<(Type Type, string Property), string[]> StringUnions = new()
    {
        [(typeof(KpiDto), "key")] = ["revenue", "orders", "aov", "conversion"],
        [(typeof(KpiDto), "format")] = ["currency", "number", "percent"],
        [(typeof(FunnelStepDto), "step")] = ["Visits", "Product views", "Added to cart", "Checkout", "Paid"],
        [(typeof(UserDto), "role")] = ["Admin"],
        [(typeof(UpdateStatusRequest), "status")] = OrderStatusValues,
        [(typeof(BulkStatusRequest), "status")] = OrderStatusValues,
        [(typeof(ProductInput), "category")] = Enum.GetNames<ProductCategory>(),
        [(typeof(AiChatTurn), "role")] = ["user", "assistant"],
        [(typeof(AskResponse), "mode")] = [AiModes.Live, AiModes.Recorded],
        [(typeof(ProductDescriptionRequest), "category")] = Enum.GetNames<ProductCategory>(),
        [(typeof(ProductDescriptionRequest), "tone")] = ["friendly", "premium", "playful"],
        [(typeof(ProductDescriptionResponse), "mode")] = [AiModes.Live, AiModes.Recorded],
    };

    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(context);

        var type = context.JsonTypeInfo.Type;
        StrictNumbers(schema);

        if ((Nullable.GetUnderlyingType(type) ?? type).IsEnum && schema.Enum is { Count: > 0 })
        {
            // JsonStringEnumConverter writes the JsonStringEnumMemberName values, always as strings.
            schema.Type = type.IsValueType && Nullable.GetUnderlyingType(type) is not null
                ? JsonSchemaType.String | JsonSchemaType.Null
                : JsonSchemaType.String;
        }

        // Runs for nested object schemas too (then JsonPropertyInfo is the referencing property); every step is idempotent.
        if (context.JsonTypeInfo.Kind == JsonTypeInfoKind.Object)
        {
            OmittedWhenNull(schema, context.JsonTypeInfo);
            AddStringUnions(schema, type);
            if (RequestTypes.Contains(type))
            {
                DescribeRequest(schema, type);
            }

            if (typeof(ProblemDetails).IsAssignableFrom(type))
            {
                DescribeProblem(schema);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// The web JSON defaults also read numbers from strings, so the generator types them as <c>integer|string</c>
    /// with a pattern. The API always writes (and the frontend always sends) real numbers.
    /// </summary>
    private static void StrictNumbers(OpenApiSchema schema)
    {
        if (schema.Type is { } t
            && (t & (JsonSchemaType.Integer | JsonSchemaType.Number)) != 0
            && (t & JsonSchemaType.String) != 0)
        {
            schema.Type = t & ~JsonSchemaType.String;
            schema.Pattern = null;
        }
    }

    /// <summary>Properties marked <c>JsonIgnore(WhenWritingNull)</c> are absent rather than null (e.g. <c>history[].note</c>).</summary>
    private static void OmittedWhenNull(OpenApiSchema schema, JsonTypeInfo typeInfo)
    {
        foreach (var property in typeInfo.Properties)
        {
            var ignore = property.AttributeProvider?
                .GetCustomAttributes(typeof(JsonIgnoreAttribute), inherit: true)
                .OfType<JsonIgnoreAttribute>()
                .FirstOrDefault();
            if (ignore?.Condition != JsonIgnoreCondition.WhenWritingNull)
            {
                continue;
            }

            schema.Required?.Remove(property.Name);
            if (schema.Properties?.TryGetValue(property.Name, out var propertySchema) == true
                && propertySchema is OpenApiSchema { Type: { } propertyType } concrete)
            {
                concrete.Type = propertyType & ~JsonSchemaType.Null;
            }
        }
    }

    private static void AddStringUnions(OpenApiSchema schema, Type type)
    {
        foreach (var (name, property) in schema.Properties ?? new Dictionary<string, IOpenApiSchema>())
        {
            if (property is OpenApiSchema concrete && StringUnions.TryGetValue((type, name), out var values))
            {
                concrete.Enum = [.. values.Select(v => (JsonNode)v)];
            }
        }
    }

    /// <summary>
    /// Request records are all-nullable so that missing fields reach the validators (422) instead of failing binding;
    /// document the shape clients are expected to send.
    /// </summary>
    private static void DescribeRequest(OpenApiSchema schema, Type type)
    {
        foreach (var (name, property) in schema.Properties ?? new Dictionary<string, IOpenApiSchema>())
        {
            if (property is not OpenApiSchema concrete)
            {
                continue;
            }

            if (concrete.Type is { } propertyType && !NullableRequestProperties.Contains((type, name)))
            {
                concrete.Type = propertyType & ~JsonSchemaType.Null;
            }

            if (OptionalRequestProperties.Contains((type, name)))
            {
                schema.Required?.Remove(name);
            }
        }

        if (type == typeof(ProductVariantInput))
        {
            schema.Description = "`id` is empty for variants added in the form; the server assigns `<productId>_v<n>`.";
        }
    }

    /// <summary>Documents the contract members every error carries on top of RFC 9457.</summary>
    private static void DescribeProblem(OpenApiSchema schema)
    {
        schema.Description =
            "RFC 9457 problem details plus the contract fields `code`, `message` and, for validation errors, `details`.";
        schema.Properties ??= new Dictionary<string, IOpenApiSchema>();
        schema.Properties["code"] = new OpenApiSchema
        {
            Type = JsonSchemaType.String,
            Description = "Stable machine-readable error code, e.g. `validation`, `not_found`, `invalid_transition`.",
        };
        schema.Properties["message"] = new OpenApiSchema
        {
            Type = JsonSchemaType.String,
            Description = "Human-readable message, safe to show to the user.",
        };
        schema.Properties["details"] = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            AdditionalProperties = new OpenApiSchema { Type = JsonSchemaType.String },
            Description = "Validation errors only: camelCase field path → first message. Omitted otherwise.",
        };
        schema.Properties["traceId"] = new OpenApiSchema
        {
            Type = JsonSchemaType.String,
            Description = "Correlates the response with the server logs.",
        };
        schema.Required ??= new HashSet<string>();
        schema.Required.Add("status");
        schema.Required.Add("code");
        schema.Required.Add("message");
    }

    /// <summary>Schema ids without the <c>Dto</c> suffix, so they read like the TypeScript models (<c>Order</c>, <c>PagedOfOrder</c>).</summary>
    public static string? CreateSchemaReferenceId(JsonTypeInfo typeInfo) =>
        OpenApiOptions.CreateDefaultSchemaReferenceId(typeInfo)?.Replace("Dto", "", StringComparison.Ordinal);
}
