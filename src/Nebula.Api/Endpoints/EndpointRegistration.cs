using Nebula.Api.Security;

namespace Nebula.Api.Endpoints;

public static class EndpointRegistration
{
    /// <summary>
    /// Maps every <c>/api</c> feature. The group requires a bearer token and is rate limited;
    /// features opt out of auth with <c>AllowAnonymous()</c>. Add one line per feature group.
    /// </summary>
    public static void MapNebulaEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var api = app.MapGroup("/api")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitSetup.ApiPolicy)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        api.MapAuthEndpoints();
        api.MapDemoEndpoints();
        // Unknown routes are answered by UseNebulaStatusCodePages (404 "Route not found"); a catch-all
        // fallback endpoint here would shadow routing's own 405 and 415 responses.
    }
}
