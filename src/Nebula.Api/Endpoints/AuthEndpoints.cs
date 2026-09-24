using Nebula.Application.Auth;

namespace Nebula.Api.Endpoints;

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var group = api.MapGroup("/auth").WithTags("Auth").AllowAnonymous();

        group.MapPost("/login", (LoginRequest? request, AuthService auth, CancellationToken ct) => auth.LoginAsync(request, ct))
            .WithName("Login")
            .WithSummary("Sign in")
            .WithDescription("Demo sign-in: any non-blank email with a password of at least 6 characters returns a JWT for the demo admin.")
            .Accepts<LoginRequest>("application/json")
            .Produces<LoginResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return api;
    }
}
