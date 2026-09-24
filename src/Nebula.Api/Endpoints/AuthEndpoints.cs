using Nebula.Api.OpenApi;
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
            .WithDescription(
                "Demo sign-in: any non-blank email with a password of at least 6 characters returns an 8-hour JWT "
                + "for the demo admin. Wrong credentials ⇒ 401 `invalid_credentials`.")
            .Accepts<LoginRequest>("application/json")
            .WithRequestExample("""{ "email": "alex@nebula.store", "password": "demo1234" }""")
            .Produces<LoginResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return api;
    }
}
