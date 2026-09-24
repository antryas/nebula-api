using Nebula.Application.Common;
using Nebula.Domain;

namespace Nebula.Application.Products;

/// <summary><c>stock</c> filter of <c>GET /api/products</c> (TS <c>StockFilter</c>); <c>low</c> means 1..10 units.</summary>
public enum StockFilter
{
    All,
    In,
    Low,
    Out,
}

/// <summary>
/// Parsed <c>GET /api/products</c> query. <paramref name="Categories"/> is null when no <c>category</c> filter was
/// sent; an empty list means a filter of only unknown values, which (like the mock) matches nothing.
/// </summary>
public sealed record ProductListQuery(ListQuery List, IReadOnlyList<ProductCategory>? Categories, StockFilter Stock)
{
    /// <summary>Upper bound of the <c>low</c> stock filter (the mock's <c>LOW_STOCK_MAX</c>).</summary>
    public const int LowStockMax = 10;

    /// <summary><c>category</c> is a comma-separated list; an unknown <c>stock</c> value means <c>all</c>.</summary>
    public static ProductListQuery Parse(ListQuery list, string? category, string? stock)
    {
        var raw = (category ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        IReadOnlyList<ProductCategory>? categories = raw.Length == 0
            ? null
            : raw.Select(c => ProductCategories.TryParse(c, out var parsed) ? parsed : (ProductCategory?)null)
                .OfType<ProductCategory>()
                .Distinct()
                .ToList();

        var filter = stock switch
        {
            "in" => StockFilter.In,
            "low" => StockFilter.Low,
            "out" => StockFilter.Out,
            _ => StockFilter.All,
        };
        return new ProductListQuery(list, categories, filter);
    }
}
