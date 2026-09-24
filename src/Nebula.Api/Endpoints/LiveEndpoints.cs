using Nebula.Application.Orders;

namespace Nebula.Api.Endpoints;

public static class LiveEndpoints
{
    public static RouteGroupBuilder MapLiveEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var group = api.MapGroup("/live").WithTags("Live");

        group.MapPost("/tick", async (LiveOrderFactory factory, CancellationToken ct) =>
            {
                var order = await factory.CreateAsync(ct);
                return TypedResults.Created($"/api/orders/{order.Id}", order);
            })
            .WithName("LiveTick")
            .WithSummary("Simulate one incoming order")
            .WithDescription(
                "Demo only: a random customer checks out 1-3 random active products now. The client decides how often to call it.")
            .Produces<OrderDto>(StatusCodes.Status201Created);

        return api;
    }
}
