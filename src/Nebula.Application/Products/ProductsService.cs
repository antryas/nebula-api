using System.Globalization;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Nebula.Application.Common;
using Nebula.Domain;

namespace Nebula.Application.Products;

/// <summary>
/// Port of <c>mock-api/handlers/products.ts</c>. Input is expected to have passed <see cref="ProductInputValidator"/>;
/// the service adds the SKU uniqueness check and derives the server-owned fields.
/// </summary>
public sealed class ProductsService(IAppDbContext db, IClock clock, WriteGate writeGate)
{
    public const string DefaultSort = "createdAt";
    public const string InvalidMessage = "Product is invalid";

    private const string IdPrefix = "prd_";

    /// <summary>Every scalar <c>Product</c> property the mock can sort by.</summary>
    public static readonly IReadOnlyDictionary<string, Expression<Func<Product, object?>>> Sortable =
        new Dictionary<string, Expression<Func<Product, object?>>>(StringComparer.Ordinal)
        {
            ["id"] = p => p.Id,
            ["sku"] = p => p.Sku,
            ["name"] = p => p.Name,
            ["description"] = p => p.Description,
            ["category"] = p => p.Category,
            ["price"] = p => p.Price,
            ["compareAtPrice"] = p => p.CompareAtPrice,
            ["imageUrl"] = p => p.ImageUrl,
            ["stock"] = p => p.Stock,
            ["sold"] = p => p.Sold,
            ["rating"] = p => p.Rating,
            ["createdAt"] = p => p.CreatedAt,
            ["active"] = p => p.Active,
        };

    public async Task<Paged<ProductDto>> ListAsync(ProductListQuery q, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);

        var query = db.Products.AsNoTracking();

        if (q.Categories is { } categories)
        {
            query = query.Where(p => categories.Contains(p.Category));
        }

        query = q.Stock switch
        {
            StockFilter.In => query.Where(p => p.Stock > ProductListQuery.LowStockMax),
            StockFilter.Low => query.Where(p => p.Stock >= 1 && p.Stock <= ProductListQuery.LowStockMax),
            StockFilter.Out => query.Where(p => p.Stock == 0),
            _ => query,
        };

        var term = q.List.Search?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(term))
        {
            query = query.Where(p => p.Name.ToLower().Contains(term) || p.Sku.ToLower().Contains(term));
        }

        return await query
            .ApplySort(q.List.Sort, q.List.Dir, Sortable, DefaultSort, p => p.Id)
            .ToPagedAsync(q.List, ProductMapping.ToDto, ct);
    }

    public async Task<ProductDto> GetAsync(string id, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("Product");
        return product.ToDto();
    }

    public async Task<ProductDto> CreateAsync(ProductInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);

        // Serialized so two concurrent requests cannot pick the same next id.
        return await writeGate.RunAsync(async () =>
        {
            await EnsureSkuIsFreeAsync(input.Sku, selfId: null, ct);

            var id = await NextIdAsync(ct);
            var product = new Product
            {
                Id = id,
                Sold = 0,
                Rating = 0,
                CreatedAt = clock.UtcNow,
                ImageUrl = $"https://picsum.photos/seed/nebula-{id}/400/400",
            };
            Apply(input, product);

            db.Products.Add(product);
            await db.SaveChangesAsync(ct);
            return product.ToDto();
        }, ct);
    }

    public async Task<ProductDto> UpdateAsync(string id, ProductInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);

        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("Product");
        await EnsureSkuIsFreeAsync(input.Sku, product.Id, ct);

        Apply(input, product);
        await db.SaveChangesAsync(ct);
        return product.ToDto();
    }

    /// <summary>Order items are snapshots, so existing orders (and analytics over them) are unaffected.</summary>
    public async Task DeleteAsync(string id, CancellationToken ct)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("Product");
        db.Products.Remove(product);
        await db.SaveChangesAsync(ct);
    }

    private async Task EnsureSkuIsFreeAsync(string? sku, string? selfId, CancellationToken ct)
    {
        var normalized = (sku ?? "").Trim().ToLowerInvariant();
        var taken = await db.Products.AnyAsync(p => p.Id != selfId && p.Sku.ToLower() == normalized, ct);
        if (taken)
        {
            throw new ValidationFailedException(
                InvalidMessage, new Dictionary<string, string> { ["sku"] = "SKU is already in use" });
        }
    }

    /// <summary>Like the mock's <c>nextProductId</c>: highest numeric suffix + 1, padded to six digits.</summary>
    private async Task<string> NextIdAsync(CancellationToken ct)
    {
        var ids = await db.Products.Select(p => p.Id).ToListAsync(ct);
        var max = ids
            .Select(id => ListQuery.ParseJsInt(id.StartsWith(IdPrefix, StringComparison.Ordinal) ? id[IdPrefix.Length..] : id))
            .Max(n => n ?? 0);
        return IdPrefix + (max + 1).ToString("D6", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Port of the mock's <c>buildProduct</c>: merges validated input into the product, keeping the server-owned
    /// fields. With variants, stock is their sum; negative stock becomes 0; a blank image keeps the current one.
    /// </summary>
    private static void Apply(ProductInput input, Product product)
    {
        var variants = (input.Variants ?? [])
            .Select((v, i) => new ProductVariant
            {
                Id = string.IsNullOrEmpty(v.Id) ? $"{product.Id}_v{i + 1}" : v.Id,
                Size = v.Size ?? "",
                Color = v.Color ?? "",
                Stock = Math.Max(0, v.Stock ?? 0),
            })
            .ToList();

        product.Sku = input.Sku!.Trim();
        product.Name = input.Name!.Trim();
        product.Description = input.Description ?? "";
        product.Category = ProductCategories.TryParse(input.Category, out var category)
            ? category
            : throw new ArgumentException("Unknown category", nameof(input));
        product.Price = Money.Round2(input.Price!.Value);
        product.CompareAtPrice = input.CompareAtPrice is { } compareAt ? Money.Round2(compareAt) : null;
        if (!string.IsNullOrEmpty(input.ImageUrl))
        {
            product.ImageUrl = input.ImageUrl;
        }

        product.Stock = variants.Count > 0 ? variants.Sum(v => v.Stock) : Math.Max(0, input.Stock ?? 0);
        product.Variants.Clear();
        product.Variants.AddRange(variants);
        product.Active = input.Active != false;
    }
}
