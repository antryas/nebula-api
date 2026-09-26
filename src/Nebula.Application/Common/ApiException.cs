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

/// <summary>Invalid input: 422 <c>validation</c> by default; the AI endpoints use 400 per their contract.</summary>
public sealed class ValidationFailedException(string message, IReadOnlyDictionary<string, string>? details = null, int status = 422)
    : ApiException(status, "validation", message, details);
