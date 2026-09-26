using System.Text.Json;
using FluentValidation;
using Nebula.Application.Common;

namespace Nebula.Api.Endpoints;

/// <summary>
/// Validates the endpoint argument of type <typeparamref name="T"/> with its registered
/// <see cref="IValidator{T}"/>; failures become <paramref name="status"/> (422 unless overridden) <c>validation</c> with the
/// first message per camelCase field.
/// </summary>
public sealed class ValidationFilter<T>(string message, int status = StatusCodes.Status422UnprocessableEntity) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var argument = context.Arguments.OfType<T>().FirstOrDefault();
        if (argument is null)
        {
            throw new ValidationFailedException(message, status: status);
        }

        var validator = context.HttpContext.RequestServices.GetRequiredService<IValidator<T>>();
        var result = await validator.ValidateAsync(argument, context.HttpContext.RequestAborted);
        if (!result.IsValid)
        {
            var details = new Dictionary<string, string>();
            foreach (var error in result.Errors)
            {
                details.TryAdd(ToCamelCasePath(error.PropertyName), error.ErrorMessage);
            }

            throw new ValidationFailedException(message, details, status);
        }

        return await next(context);
    }

    /// <summary>"Variants[0].Size" → "variants[0].size".</summary>
    internal static string ToCamelCasePath(string propertyName) =>
        string.Join('.', propertyName.Split('.').Select(JsonNamingPolicy.CamelCase.ConvertName));
}

public static class ValidationFilterExtensions
{
    /// <summary>Validates the <typeparamref name="T"/> argument before the handler runs.</summary>
    public static RouteHandlerBuilder WithValidation<T>(
        this RouteHandlerBuilder builder,
        string message = "Request is invalid",
        int status = StatusCodes.Status422UnprocessableEntity) =>
        builder
            .AddEndpointFilter(new ValidationFilter<T>(message, status))
            .ProducesProblem(status);
}
