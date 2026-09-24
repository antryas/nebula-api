using Nebula.Domain;

namespace Nebula.Application.Products;

/// <summary>
/// Body of <c>POST/PUT /api/products</c>: the TS <c>ProductInput</c> (<c>Product</c> without <c>id</c>, <c>sold</c>,
/// <c>rating</c>, <c>createdAt</c>) as built by <c>toProductInput</c> in the product form. Everything is nullable and
/// <see cref="Category"/> is a plain string so that missing or unknown values reach the validator (422) instead of
/// failing JSON binding.
/// </summary>
public sealed record ProductInput(
    string? Sku,
    string? Name,
    string? Description,
    string? Category,
    decimal? Price,
    decimal? CompareAtPrice,
    string? ImageUrl,
    int? Stock,
    IReadOnlyList<ProductVariantInput>? Variants,
    bool? Active);

/// <summary>A variant as the form sends it; <see cref="Id"/> is empty for variants added in the form.</summary>
public sealed record ProductVariantInput(string? Id, string? Size, string? Color, int? Stock);

/// <summary>Product category wire values, exactly the frontend <c>ProductCategory</c> union.</summary>
public static class ProductCategories
{
    private static readonly Dictionary<string, ProductCategory> ByWireName =
        Enum.GetValues<ProductCategory>().ToDictionary(c => c.ToString(), StringComparer.Ordinal);

    /// <summary>Case-sensitive and name-only (numeric strings are rejected), like the mock's <c>CATEGORIES.includes</c>.</summary>
    public static bool TryParse(string? value, out ProductCategory category) =>
        ByWireName.TryGetValue(value ?? "", out category);
}
