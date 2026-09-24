using Nebula.Application.Common;
using Nebula.Application.Customers;

namespace Nebula.Api.Endpoints;

public static class CustomersEndpoints
{
    /// <summary>Raw <c>GET /api/customers</c> query; parsed leniently like the mock.</summary>
    public sealed record CustomerListParameters(string? Page, string? PageSize, string? Sort, string? Dir, string? Search)
    {
        public ListQuery ToQuery() => ListQuery.Parse(Page, PageSize, Sort, Dir, Search);
    }

    public static RouteGroupBuilder MapCustomersEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var group = api.MapGroup("/customers").WithTags("Customers");

        group.MapGet("/", ([AsParameters] CustomerListParameters p, CustomersService customers, CancellationToken ct) =>
                customers.ListAsync(p.ToQuery(), ct))
            .WithName("ListCustomers")
            .WithSummary("List customers")
            .WithDescription(
                "Paged customers. `search` matches name, email and country. Default sort `createdAt` desc; "
                + "page size 1..100 (default 20).")
            .Produces<Paged<CustomerDto>>();

        group.MapGet("/{id}", (string id, CustomersService customers, CancellationToken ct) => customers.GetAsync(id, ct))
            .WithName("GetCustomer")
            .WithSummary("Get a customer profile")
            .WithDescription("The customer and all of their orders, newest first.")
            .Produces<CustomerProfileDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return api;
    }
}
