namespace Nebula.Application.Common;

/// <summary>Business error translated by the API layer into a ProblemDetails response.</summary>
public class ApiException(int status, string code, string message, IReadOnlyDictionary<string, string>? details = null)
    : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public IReadOnlyDictionary<string, string>? Details { get; } = details;
}

public sealed class NotFoundException(string resource)
    : ApiException(404, "not_found", $"{resource} not found");

public sealed class ValidationFailedException(string message, IReadOnlyDictionary<string, string>? details = null)
    : ApiException(422, "validation", message, details);
