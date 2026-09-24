using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Nebula.Application.Common;

namespace Nebula.Api.Errors;

/// <summary>Translates exceptions into contract-shaped problem responses.</summary>
public sealed partial class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        switch (exception)
        {
            case ApiException api:
                await ProblemDetailsSetup.WriteAsync(httpContext, api.Status, api.Code, api.Message, api.Details);
                return true;

            case BadHttpRequestException bad:
                LogBadRequest(logger, bad.StatusCode, bad.Message);
                await ProblemDetailsSetup.WriteAsync(
                    httpContext, bad.StatusCode, ErrorCodes.ForStatus(bad.StatusCode), DescribeBadRequest(bad));
                return true;

            case OperationCanceledException when httpContext.RequestAborted.IsCancellationRequested:
                // The client went away; nobody is listening for a body.
                return true;

            default:
                LogUnhandled(logger, exception, httpContext.Request.Method, httpContext.Request.Path.Value ?? "");
                await ProblemDetailsSetup.WriteAsync(
                    httpContext, StatusCodes.Status500InternalServerError, "server_error", "Something went wrong on our side");
                return true;
        }
    }

    private static string DescribeBadRequest(BadHttpRequestException exception) => exception.StatusCode switch
    {
        StatusCodes.Status413PayloadTooLarge => "Request body is too large",
        StatusCodes.Status415UnsupportedMediaType => "Content type must be application/json",
        _ when exception.InnerException is JsonException => "Request body is not valid JSON",
        _ => "The request could not be understood",
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "Rejected bad request ({Status}): {Reason}")]
    private static partial void LogBadRequest(ILogger logger, int status, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception for {Method} {Path}")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string method, string path);
}
