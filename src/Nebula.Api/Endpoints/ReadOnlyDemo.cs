using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Nebula.Application.Common;

namespace Nebula.Api.Endpoints;

/// <summary>
/// Read-only public demo (<c>Demo:ReadOnly</c>, on unless configured off): visitor writes run for real inside a
/// transaction that is rolled back, so they are validated and answered exactly like a real write, but the shared
/// database never changes. Such responses carry <c>X-Nebula-Dry-Run: true</c>.
/// </summary>
public static class ReadOnlyDemo
{
    public const string ConfigKey = "Demo:ReadOnly";
    public const string DryRunHeader = "X-Nebula-Dry-Run";

    private const string HeaderDescription =
        "`true` when the demo runs read-only: the request was validated and executed, but nothing was saved.";

    /// <summary>Read per request so hosts (and tests) can override <c>Demo:ReadOnly</c> after registration.</summary>
    public static bool IsEnabled(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        return httpContext.RequestServices.GetRequiredService<IConfiguration>().GetValue(ConfigKey, true);
    }

    /// <summary>
    /// Adds the dry-run header when the response starts, so it also survives the exception handler clearing
    /// the response for errors (validation, not found, ...).
    /// </summary>
    public static void MarkDryRun(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        httpContext.Response.OnStarting(() =>
        {
            httpContext.Response.Headers[DryRunHeader] = "true";
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Dry-runs every mutating endpoint of the group (anything but GET, HEAD and OPTIONS) in read-only mode, and
    /// documents that on those operations. With read-only mode off the writes still go through the <see cref="WriteGate"/>.
    /// </summary>
    public static RouteGroupBuilder WithDryRunWhenReadOnly(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);
        group.AddEndpointFilter(new DryRunFilter());
        group.AddOpenApiOperationTransformer((operation, context, _) =>
        {
            if (IsMutating(context.Description.HttpMethod ?? HttpMethods.Get))
            {
                DocumentDryRun(operation, "In the read-only public demo the change is validated and executed, then rolled back");
            }

            return Task.CompletedTask;
        });
        return group;
    }

    /// <summary>Appends <paramref name="note"/> to the description and declares the header on the endpoint's own responses.</summary>
    public static void DocumentDryRun(OpenApiOperation operation, string note)
    {
        ArgumentNullException.ThrowIfNull(operation);
        operation.Description = $"{operation.Description} {note}; the response carries `{DryRunHeader}: true`.".TrimStart();

        foreach (var (status, response) in operation.Responses ?? [])
        {
            // 401 and 429 are answered before the endpoint runs.
            if (status is "401" or "429" || response is not OpenApiResponse concrete)
            {
                continue;
            }

            concrete.Headers ??= new Dictionary<string, IOpenApiHeader>();
            concrete.Headers[DryRunHeader] = new OpenApiHeader
            {
                Description = HeaderDescription,
                Schema = new OpenApiSchema { Type = JsonSchemaType.String, Enum = ["true"] },
            };
        }
    }

    internal static bool IsMutating(string method) =>
        !(HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method));

    /// <summary>
    /// Runs the rest of the pipeline (validation, handler, services) of a mutating endpoint inside <see cref="IDryRunner"/>
    /// in read-only mode. The handler's result is fully built before the rollback (DTOs are plain records), so the
    /// response is what a real write returns. Otherwise the write still takes the <see cref="WriteGate"/>, so every
    /// SQLite writer goes through it in both modes.
    /// </summary>
    private sealed class DryRunFilter : IEndpointFilter
    {
        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(next);

            var httpContext = context.HttpContext;
            if (!IsMutating(httpContext.Request.Method))
            {
                return await next(context);
            }

            if (!IsEnabled(httpContext))
            {
                var writeGate = httpContext.RequestServices.GetRequiredService<WriteGate>();
                return await writeGate.RunAsync(() => next(context).AsTask(), httpContext.RequestAborted);
            }

            MarkDryRun(httpContext);
            var dryRunner = httpContext.RequestServices.GetRequiredService<IDryRunner>();
            return await dryRunner.RunAndRollBackAsync(() => next(context).AsTask(), httpContext.RequestAborted);
        }
    }
}
