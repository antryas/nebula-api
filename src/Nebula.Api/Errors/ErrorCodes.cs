namespace Nebula.Api.Errors;

/// <summary>Maps HTTP status codes to the contract's machine-readable error codes.</summary>
public static class ErrorCodes
{
    public static string ForStatus(int status) => status switch
    {
        StatusCodes.Status400BadRequest => "bad_request",
        StatusCodes.Status401Unauthorized => "unauthorized",
        StatusCodes.Status403Forbidden => "forbidden",
        StatusCodes.Status404NotFound => "not_found",
        StatusCodes.Status405MethodNotAllowed => "method_not_allowed",
        StatusCodes.Status413PayloadTooLarge => "payload_too_large",
        StatusCodes.Status415UnsupportedMediaType => "unsupported_media_type",
        StatusCodes.Status422UnprocessableEntity => "validation",
        StatusCodes.Status429TooManyRequests => "rate_limited",
        _ => "server_error",
    };
}
