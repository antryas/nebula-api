using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Nebula.Api.Health;

/// <summary>Writes the health report as <c>{"status":"Healthy"}</c>.</summary>
public static class HealthResponse
{
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(report);

        context.Response.ContentType = "application/json; charset=utf-8";
        return JsonSerializer.SerializeAsync(
            context.Response.Body,
            new HealthBody(report.Status.ToString()),
            HealthJsonContext.Default.HealthBody,
            context.RequestAborted);
    }
}

public sealed record HealthBody(string Status);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(HealthBody))]
internal sealed partial class HealthJsonContext : JsonSerializerContext;
