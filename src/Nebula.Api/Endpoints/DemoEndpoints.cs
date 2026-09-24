using Nebula.Application.Common;

namespace Nebula.Api.Endpoints;

public static class DemoEndpoints
{
    public static RouteGroupBuilder MapDemoEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var group = api.MapGroup("/demo").WithTags("Demo");

        group.MapPost("/reset", async (IDemoResetter resetter, CancellationToken ct) =>
            {
                await resetter.ResetAsync(ct);
                return TypedResults.NoContent();
            })
            .WithName("ResetDemo")
            .WithSummary("Reset demo data")
            .WithDescription("Re-seeds the database with the deterministic demo data set.")
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return api;
    }
}
