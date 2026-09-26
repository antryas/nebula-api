using Nebula.Api.OpenApi;
using Nebula.Application.Common;

namespace Nebula.Api.Endpoints;

public static class DemoEndpoints
{
    /// <summary>Body of <c>GET /api/demo/mode</c>.</summary>
    public sealed record DemoModeResponse(bool ReadOnly);

    public static RouteGroupBuilder MapDemoEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var group = api.MapGroup("/demo").WithTags("Demo");

        group.MapGet("/mode", (HttpContext http) => new DemoModeResponse(ReadOnlyDemo.IsEnabled(http)))
            .WithName("GetDemoMode")
            .WithSummary("Demo mode")
            .WithDescription(
                "`readOnly` is true on the public demo: writes are validated and answered like real ones, but rolled "
                + $"back, and their responses carry `{ReadOnlyDemo.DryRunHeader}: true`. The live order feed still persists.")
            .WithResponseExample("""{ "readOnly": true }""")
            .Produces<DemoModeResponse>();

        group.MapPost("/reset", async (HttpContext http, IDemoResetter resetter, CancellationToken ct) =>
            {
                // Visitors cannot re-seed the shared database; the periodic reset still runs.
                if (ReadOnlyDemo.IsEnabled(http))
                {
                    ReadOnlyDemo.MarkDryRun(http);
                }
                else
                {
                    await resetter.ResetAsync(ct);
                }

                return TypedResults.NoContent();
            })
            .WithName("ResetDemo")
            .WithSummary("Reset demo data")
            .WithDescription("Re-seeds the database with the deterministic demo data set.")
            .AddOpenApiOperationTransformer((operation, _, _) =>
            {
                ReadOnlyDemo.DocumentDryRun(operation, "In the read-only public demo this is a no-op");
                return Task.CompletedTask;
            })
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return api;
    }
}
