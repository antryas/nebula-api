using System.Diagnostics;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Nebula.Api.Errors;

/// <summary>
/// Every error leaves the API as <c>application/problem+json</c>: a standard ProblemDetails
/// plus the contract fields <c>code</c>, <c>message</c> and optional <c>details</c>.
/// </summary>
public static class ProblemDetailsSetup
{
    private const string ProblemContentType = "application/problem+json";

    public static IServiceCollection AddNebulaProblemDetails(this IServiceCollection services)
    {
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
            Complete(context.ProblemDetails, context.HttpContext));
        services.AddExceptionHandler<ApiExceptionHandler>();
        // Malformed bodies throw BadHttpRequestException so the exception handler can describe them.
        services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
        return services;
    }

    /// <summary>
    /// Turns empty 4xx/5xx responses (unknown routes, 403, 405, 415, ...) into contract-shaped problems.
    /// </summary>
    public static IApplicationBuilder UseNebulaStatusCodePages(this IApplicationBuilder app) =>
        app.UseStatusCodePages(context =>
        {
            var httpContext = context.HttpContext;
            var status = httpContext.Response.StatusCode;
            var message = status == StatusCodes.Status404NotFound && httpContext.GetEndpoint() is null
                ? "Route not found"
                : ReasonPhrases.GetReasonPhrase(status);
            return WriteAsync(httpContext, status, ErrorCodes.ForStatus(status), message);
        });

    /// <summary>Writes a contract-shaped problem response.</summary>
    public static async Task WriteAsync(
        HttpContext httpContext,
        int status,
        string code,
        string message,
        IReadOnlyDictionary<string, string>? details = null)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var problem = new ProblemDetails { Status = status, Detail = message };
        problem.Extensions["code"] = code;
        problem.Extensions["message"] = message;
        if (details is not null)
        {
            problem.Extensions["details"] = details;
        }

        Complete(problem, httpContext);
        httpContext.Response.StatusCode = status;

        var service = httpContext.RequestServices.GetService<IProblemDetailsService>();
        if (service is not null
            && await service.TryWriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problem }))
        {
            return;
        }

        // The default writer declines when the client's Accept header excludes JSON; answer in JSON anyway.
        var jsonOptions = httpContext.RequestServices.GetService<IOptions<HttpJsonOptions>>()?.Value.SerializerOptions;
        await httpContext.Response.WriteAsJsonAsync(
            problem, jsonOptions, ProblemContentType, httpContext.RequestAborted);
    }

    private static void Complete(ProblemDetails problem, HttpContext httpContext)
    {
        var status = problem.Status ?? httpContext.Response.StatusCode;
        problem.Status = status;
        problem.Title ??= ReasonPhrases.GetReasonPhrase(status);
        problem.Extensions.TryAdd("code", ErrorCodes.ForStatus(status));
        problem.Extensions.TryAdd("message", problem.Detail ?? problem.Title);
        problem.Extensions["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier;
    }
}
