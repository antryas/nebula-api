using Nebula.Domain;

namespace Nebula.Application.Products;

/// <summary>Wire shape of the TS <c>ProductVariant</c> interface.</summary>
public sealed record ProductVariantDto(string Id, string Size, string Color, int Stock);

/// <summary>Wire shape of the TS <c>Product</c> interface.</summary>
public sealed record ProductDto(
    string Id,
    string Sku,
    string Name,
    string Description,
    ProductCategory Category,
    decimal Price,
    decimal? CompareAtPrice,
    string ImageUrl,
    int Stock,
    int Sold,
    double Rating,
    IReadOnlyList<ProductVariantDto> Variants,
    DateTime CreatedAt,
    bool Active);

public static class ProductMapping
{
    public static ProductDto ToDto(this Product p)
    {
        ArgumentNullException.ThrowIfNull(p);
        return new ProductDto(
            p.Id,
            p.Sku,
            p.Name,
            p.Description,
            p.Category,
            p.Price,
            p.CompareAtPrice,
            p.ImageUrl,
            p.Stock,
            p.Sold,
            p.Rating,
            [.. p.Variants.Select(v => new ProductVariantDto(v.Id, v.Size, v.Color, v.Stock))],
            p.CreatedAt,
            p.Active);
    }
}
