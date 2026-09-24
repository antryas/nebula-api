using Microsoft.AspNetCore.Mvc;
using Nebula.Api.OpenApi;
using Nebula.Application.Common;
using Nebula.Application.Orders;

namespace Nebula.Api.Endpoints;

public static class OrdersEndpoints
{
    /// <summary>Raw <c>GET /api/orders</c> query; parsed leniently like the mock.</summary>
    public sealed record OrderListParameters(
        [FromQuery(Name = "page")] string? Page,
        [FromQuery(Name = "pageSize")] string? PageSize,
        [FromQuery(Name = "sort")] string? Sort,
        [FromQuery(Name = "dir")] string? Dir,
        [FromQuery(Name = "search")] string? Search,
        [FromQuery(Name = "status")] string? Status,
        [FromQuery(Name = "from")] string? From,
        [FromQuery(Name = "to")] string? To)
    {
        public OrderListQuery ToQuery() =>
            OrderListQuery.Parse(ListQuery.Parse(Page, PageSize, Sort, Dir, Search), Status, From, To);
    }

    public static RouteGroupBuilder MapOrdersEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var group = api.MapGroup("/orders").WithTags("Orders");

        group.MapGet("/", ([AsParameters] OrderListParameters p, OrdersService orders, CancellationToken ct) =>
                orders.ListAsync(p.ToQuery(), ct))
            .WithName("ListOrders")
            .WithSummary("List orders")
            .WithDescription(
                "Paged orders. `status` is a comma-separated list, `from`/`to` are inclusive ISO timestamps, `search` "
                + "matches number, customer name and email. Default sort `createdAt` desc; page size 1..100 (default 20).")
            .Produces<Paged<OrderDto>>();

        group.MapGet("/{id}", (string id, OrdersService orders, CancellationToken ct) => orders.GetAsync(id, ct))
            .WithName("GetOrder")
            .WithSummary("Get an order")
            .WithDescription("A single order with its items, shipping address and status history.")
            .Produces<OrderDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPatch("/{id}/status", (string id, UpdateStatusRequest? request, OrdersService orders, CancellationToken ct) =>
                orders.UpdateStatusAsync(id, request, ct))
            .WithName("UpdateOrderStatus")
            .WithSummary("Change an order's status")
            .WithDescription("Appends a history entry. Delivered and cancelled orders are closed (422 `invalid_transition`).")
            .Accepts<UpdateStatusRequest>("application/json")
            .WithRequestExample("""{ "status": "shipped" }""")
            .Produces<OrderDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/bulk-status", (BulkStatusRequest? request, OrdersService orders, CancellationToken ct) =>
                orders.BulkUpdateStatusAsync(request, ct))
            .WithName("BulkUpdateOrderStatus")
            .WithSummary("Change the status of several orders")
            .WithDescription("Closed and unknown orders are skipped; `updated` counts the others.")
            .Accepts<BulkStatusRequest>("application/json")
            .WithRequestExample("""{ "ids": ["ord_004800", "ord_004799"], "status": "packing" }""")
            .Produces<BulkStatusResult>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return api;
    }
}
