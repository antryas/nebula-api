using Microsoft.EntityFrameworkCore;
using Nebula.Application.Common;
using Nebula.Application.Products;
using Nebula.Domain;
using Nebula.Infrastructure.Persistence;
using Nebula.UnitTests.TestSupport;

namespace Nebula.UnitTests.Products;

public sealed class ProductsServiceTests : IAsyncLifetime
{
    private SqliteTestDb _db = null!;

    /// <summary>
    /// On top of the three Apparel products (stock 10, created 100 days ago) of <see cref="SqliteTestDb"/>:
    /// a low-stock Footwear product with variants and an out-of-stock Home product, both newer. prd_0002 gets
    /// 11 units, just above the low-stock range, while the others keep 10, its upper bound.
    /// </summary>
    public async ValueTask InitializeAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        _db = await SqliteTestDb.CreateAsync(ct);
        await using var context = _db.CreateContext();
        (await context.Products.SingleAsync(p => p.Id == "prd_0002", ct)).Stock = 11;
        context.Products.AddRange(
            new Product
            {
                Id = "prd_0004",
                Sku = "FOO-RUN-004",
                Name = "Trail Runner",
                Category = ProductCategory.Footwear,
                Price = 120m,
                ImageUrl = "https://img.test/4.jpg",
                Stock = 3,
                Sold = 7,
                Rating = 4.2,
                CreatedAt = SqliteTestDb.Now.AddDays(-2),
                Active = true,
                Variants =
                [
                    new() { Id = "prd_0004_v1", Size = "42", Color = "Black", Stock = 1 },
                    new() { Id = "prd_0004_v2", Size = "43", Color = "Black", Stock = 2 },
                ],
            },
            new Product
            {
                Id = "prd_0005",
                Sku = "HOM-LMP-005",
                Name = "desk Lamp",
                Category = ProductCategory.Home,
                Price = 35m,
                ImageUrl = "https://img.test/5.jpg",
                Stock = 0,
                CreatedAt = SqliteTestDb.Now.AddDays(-1),
                Active = true,
            });
        await context.SaveChangesAsync(ct);
    }

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    private static ProductsService Service(AppDbContext context) => new(context, new FakeClock(SqliteTestDb.Now));

    private static ProductListQuery Query(
        string? category = null, string? stock = null, string? search = null, string? sort = null, string? dir = null) =>
        ProductListQuery.Parse(ListQuery.Parse(null, null, sort, dir, search), category, stock);

    private async Task<List<string>> ListIdsAsync(ProductListQuery q)
    {
        await using var context = _db.CreateContext();
        var page = await Service(context).ListAsync(q, TestContext.Current.CancellationToken);
        return [.. page.Items.Select(p => p.Id)];
    }

    private async Task<T> RunAsync<T>(Func<ProductsService, CancellationToken, Task<T>> action)
    {
        await using var context = _db.CreateContext();
        return await action(Service(context), TestContext.Current.CancellationToken);
    }

    private async Task RunAsync(Func<ProductsService, CancellationToken, Task> action)
    {
        await using var context = _db.CreateContext();
        await action(Service(context), TestContext.Current.CancellationToken);
    }

    private static ProductInput Input(string sku = "NEW-SKU-01") => ProductInputValidatorTests.Valid() with { Sku = sku };

    [Fact]
    public async Task Default_list_is_newest_first_with_id_tie_breaker()
    {
        Assert.Equal(["prd_0005", "prd_0004", "prd_0003", "prd_0002", "prd_0001"], await ListIdsAsync(Query()));
    }

    [Fact]
    public async Task Filters_by_category_list()
    {
        Assert.Equal(["prd_0005", "prd_0004"], await ListIdsAsync(Query(category: "Footwear, Home,Toys")));
    }

    [Fact]
    public async Task Category_filter_with_only_unknown_values_matches_nothing()
    {
        Assert.Empty(await ListIdsAsync(Query(category: "Toys")));
    }

    [Theory]
    [InlineData("in", new[] { "prd_0002" })]
    [InlineData("low", new[] { "prd_0004", "prd_0003", "prd_0001" })]
    [InlineData("out", new[] { "prd_0005" })]
    [InlineData("all", new[] { "prd_0005", "prd_0004", "prd_0003", "prd_0002", "prd_0001" })]
    [InlineData("bogus", new[] { "prd_0005", "prd_0004", "prd_0003", "prd_0002", "prd_0001" })]
    public async Task Filters_by_stock_level(string stock, string[] expected)
    {
        Assert.Equal(expected, await ListIdsAsync(Query(stock: stock)));
    }

    [Theory]
    [InlineData("TRAIL", "prd_0004")]
    [InlineData("hom-lmp", "prd_0005")]
    public async Task Searches_name_and_sku_case_insensitively(string term, string expected)
    {
        Assert.Equal([expected], await ListIdsAsync(Query(search: term)));
    }

    [Fact]
    public async Task Sorts_by_name_case_insensitively()
    {
        // Canvas Sneaker, Cotton Tee, desk Lamp, Old Lamp, Trail Runner
        Assert.Equal(
            ["prd_0002", "prd_0001", "prd_0005", "prd_0003", "prd_0004"],
            await ListIdsAsync(Query(sort: "name", dir: "asc")));
    }

    [Fact]
    public async Task Sorts_by_category_name()
    {
        var ids = await ListIdsAsync(Query(sort: "category", dir: "desc"));

        // Home > Footwear > Apparel
        Assert.Equal(["prd_0005", "prd_0004"], ids[..2]);
    }

    [Fact]
    public async Task Get_returns_product_with_variants()
    {
        var product = await RunAsync((s, ct) => s.GetAsync("prd_0004", ct));

        Assert.Equal("Trail Runner", product.Name);
        Assert.Equal(["prd_0004_v1", "prd_0004_v2"], product.Variants.Select(v => v.Id));
    }

    [Fact]
    public async Task Get_unknown_throws_not_found()
    {
        var ex = await Assert.ThrowsAsync<NotFoundException>(() => RunAsync((s, ct) => s.GetAsync("prd_9999", ct)));

        Assert.Equal("Product not found", ex.Message);
    }

    [Fact]
    public async Task Create_assigns_server_fields_like_the_mock()
    {
        var input = Input() with
        {
            Sku = "  NEW-SKU-01 ",
            Name = "  Linen Shirt ",
            Description = null,
            Price = 19.999m,
            CompareAtPrice = 29.994m,
            ImageUrl = "",
            Stock = -4,
            Variants = null,
            Active = null,
        };

        var created = await RunAsync((s, ct) => s.CreateAsync(input, ct));

        Assert.Equal("prd_000006", created.Id);
        Assert.Equal("NEW-SKU-01", created.Sku);
        Assert.Equal("Linen Shirt", created.Name);
        Assert.Equal("", created.Description);
        Assert.Equal(ProductCategory.Apparel, created.Category);
        Assert.Equal(20m, created.Price);
        Assert.Equal(29.99m, created.CompareAtPrice);
        Assert.Equal("https://picsum.photos/seed/nebula-prd_000006/400/400", created.ImageUrl);
        Assert.Equal(0, created.Stock);
        Assert.Equal(0, created.Sold);
        Assert.Equal(0, created.Rating);
        Assert.Empty(created.Variants);
        Assert.Equal(SqliteTestDb.Now, created.CreatedAt);
        Assert.True(created.Active);

        var stored = await RunAsync((s, ct) => s.GetAsync("prd_000006", ct));
        Assert.Equal(created with { Variants = [] }, stored with { Variants = [] });
    }

    [Fact]
    public async Task Create_derives_stock_from_variants_and_generates_missing_variant_ids()
    {
        var input = Input() with
        {
            Stock = 99,
            ImageUrl = "https://img.test/custom.jpg",
            Active = false,
            Variants =
            [
                new ProductVariantInput("", " S ", "Red", 2),
                new ProductVariantInput("keep_me", "M", "Red", 5),
                new ProductVariantInput(null, null, null, -3),
            ],
        };

        var created = await RunAsync((s, ct) => s.CreateAsync(input, ct));

        Assert.Equal(7, created.Stock);
        Assert.Equal("https://img.test/custom.jpg", created.ImageUrl);
        Assert.False(created.Active);
        Assert.Equal(
            [
                new ProductVariantDto("prd_000006_v1", " S ", "Red", 2),
                new ProductVariantDto("keep_me", "M", "Red", 5),
                new ProductVariantDto("prd_000006_v3", "", "", 0),
            ],
            created.Variants);
    }

    [Fact]
    public async Task Consecutive_creates_get_increasing_ids()
    {
        await RunAsync((s, ct) => s.CreateAsync(Input("NEW-SKU-01"), ct));
        var second = await RunAsync((s, ct) => s.CreateAsync(Input("NEW-SKU-02"), ct));

        Assert.Equal("prd_000007", second.Id);
    }

    [Fact]
    public async Task New_products_are_listed_first_by_default()
    {
        await RunAsync((s, ct) => s.CreateAsync(Input(), ct));

        Assert.Equal("prd_000006", (await ListIdsAsync(Query()))[0]);
    }

    [Theory]
    [InlineData("SKU-0002")]
    [InlineData(" sku-0002 ")]
    public async Task Create_rejects_a_sku_in_use(string sku)
    {
        var ex = await Assert.ThrowsAsync<ValidationFailedException>(
            () => RunAsync((s, ct) => s.CreateAsync(Input(sku), ct)));

        Assert.Equal("Product is invalid", ex.Message);
        Assert.Equal("SKU is already in use", ex.Details!["sku"]);
    }

    [Fact]
    public async Task Update_replaces_editable_fields_and_keeps_server_fields()
    {
        var input = Input("FOO-RUN-004") with
        {
            Name = "Trail Runner 2",
            Category = "Footwear",
            ImageUrl = null,
            Variants = [new ProductVariantInput("prd_0004_v2", "44", "Blue", 4)],
        };

        var updated = await RunAsync((s, ct) => s.UpdateAsync("prd_0004", input, ct));

        Assert.Equal("prd_0004", updated.Id);
        Assert.Equal("Trail Runner 2", updated.Name);
        Assert.Equal(7, updated.Sold);
        Assert.Equal(4.2, updated.Rating);
        Assert.Equal(SqliteTestDb.Now.AddDays(-2), updated.CreatedAt);
        Assert.Equal("https://img.test/4.jpg", updated.ImageUrl);
        Assert.Equal(4, updated.Stock);
        Assert.Equal([new ProductVariantDto("prd_0004_v2", "44", "Blue", 4)], updated.Variants);

        var stored = await RunAsync((s, ct) => s.GetAsync("prd_0004", ct));
        Assert.Equal(updated.Variants, stored.Variants);
        Assert.Equal(updated with { Variants = [] }, stored with { Variants = [] });
    }

    [Fact]
    public async Task Update_may_keep_its_own_sku_in_different_case()
    {
        var updated = await RunAsync((s, ct) => s.UpdateAsync("prd_0005", Input("hom-lmp-005"), ct));

        Assert.Equal("hom-lmp-005", updated.Sku);
    }

    [Fact]
    public async Task Update_rejects_another_products_sku()
    {
        var ex = await Assert.ThrowsAsync<ValidationFailedException>(
            () => RunAsync((s, ct) => s.UpdateAsync("prd_0005", Input("foo-run-004"), ct)));

        Assert.Equal("SKU is already in use", ex.Details!["sku"]);
    }

    [Fact]
    public async Task Update_unknown_throws_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => RunAsync((s, ct) => s.UpdateAsync("prd_9999", Input(), ct)));
    }

    [Fact]
    public async Task Delete_removes_the_product_and_keeps_order_snapshots()
    {
        var ct = TestContext.Current.CancellationToken;

        await RunAsync((s, c) => s.DeleteAsync("prd_0004", c));
        await RunAsync((s, c) => s.DeleteAsync("prd_0001", c));

        await using var context = _db.CreateContext();
        Assert.Equal(["prd_0002", "prd_0003", "prd_0005"], await context.Products.Select(p => p.Id).Order().ToListAsync(ct));
        var orders = await context.Orders.AsNoTracking().ToListAsync(ct);
        Assert.Equal(5, orders.Count);
        Assert.All(orders, o => Assert.Equal("prd_0001", Assert.Single(o.Items).ProductId));
    }

    [Fact]
    public async Task Delete_unknown_throws_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => RunAsync((s, ct) => s.DeleteAsync("prd_9999", ct)));
    }
}
