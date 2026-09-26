using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;

namespace Nebula.Api.OpenApi;

public static class OpenApiExampleExtensions
{
    /// <summary>Pre-fills Swagger UI's "Try it out" body for this endpoint with <paramref name="json"/>.</summary>
    public static RouteHandlerBuilder WithRequestExample(this RouteHandlerBuilder builder, string json)
    {
        var example = JsonNode.Parse(json);
        return builder.AddOpenApiOperationTransformer((operation, _, _) =>
        {
            if (operation.RequestBody?.Content is { } content)
            {
                foreach (var mediaType in content.Values)
                {
                    mediaType.Example = example?.DeepClone();
                }
            }

            return Task.CompletedTask;
        });
    }

    /// <summary>Shows <paramref name="json"/> as the example of the endpoint's 200 response.</summary>
    public static RouteHandlerBuilder WithResponseExample(this RouteHandlerBuilder builder, string json)
    {
        var example = JsonNode.Parse(json);
        return builder.AddOpenApiOperationTransformer((operation, _, _) =>
        {
            if (operation.Responses?.GetValueOrDefault("200")?.Content is { } content)
            {
                foreach (var mediaType in content.Values)
                {
                    mediaType.Example = example?.DeepClone();
                }
            }

            return Task.CompletedTask;
        });
    }
}
