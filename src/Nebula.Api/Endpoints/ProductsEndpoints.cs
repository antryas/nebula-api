using Nebula.Application.Common;
using Nebula.Application.Products;

namespace Nebula.Api.Endpoints;

public static class ProductsEndpoints
{
    /// <summary>Raw <c>GET /api/products</c> query; parsed leniently like the mock.</summary>
    public sealed record ProductListParameters(
        string? Page, string? PageSize, string? Sort, string? Dir, string? Search, string? Category, string? Stock)
    {
        public ProductListQuery ToQuery() =>
            ProductListQuery.Parse(ListQuery.Parse(Page, PageSize, Sort, Dir, Search), Category, Stock);
    }

    public static RouteGroupBuilder MapProductsEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var group = api.MapGroup("/products").WithTags("Products");

        group.MapGet("/", ([AsParameters] ProductListParameters p, ProductsService products, CancellationToken ct) =>
                products.ListAsync(p.ToQuery(), ct))
            .WithName("ListProducts")
            .WithSummary("List products")
            .WithDescription(
                "Paged products. `category` is a comma-separated list, `stock` is `all|in|low|out` (`low` = 1..10 units), "
                + "`search` matches name and SKU. Default sort `createdAt` desc; page size 1..100 (default 20).")
            .Produces<Paged<ProductDto>>();

        group.MapGet("/{id}", (string id, ProductsService products, CancellationToken ct) => products.GetAsync(id, ct))
            .WithName("GetProduct")
            .WithSummary("Get a product")
            .Produces<ProductDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", async (ProductInput? input, ProductsService products, CancellationToken ct) =>
            {
                var created = await products.CreateAsync(input!, ct);
                return TypedResults.Created($"/api/products/{Uri.EscapeDataString(created.Id)}", created);
            })
            .WithName("CreateProduct")
            .WithSummary("Create a product")
            .WithDescription(
                "Server assigns `id`, `sold`, `rating`, `createdAt` and a placeholder image when `imageUrl` is blank. "
                + "With variants, `stock` is their sum. Invalid input or a SKU in use ⇒ 422 `validation` with field details.")
            .Accepts<ProductInput>("application/json")
            .WithValidation<ProductInput>(ProductsService.InvalidMessage)
            .Produces<ProductDto>(StatusCodes.Status201Created);

        group.MapPut("/{id}", (string id, ProductInput? input, ProductsService products, CancellationToken ct) =>
                products.UpdateAsync(id, input!, ct))
            .WithName("UpdateProduct")
            .WithSummary("Update a product")
            .WithDescription("Replaces the editable fields; `id`, `sold`, `rating` and `createdAt` are kept.")
            .Accepts<ProductInput>("application/json")
            .WithValidation<ProductInput>(ProductsService.InvalidMessage)
            .Produces<ProductDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{id}", async (string id, ProductsService products, CancellationToken ct) =>
            {
                await products.DeleteAsync(id, ct);
                return TypedResults.NoContent();
            })
            .WithName("DeleteProduct")
            .WithSummary("Delete a product")
            .WithDescription("Existing orders keep their item snapshots.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return api;
    }
}
