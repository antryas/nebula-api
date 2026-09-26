using System.ComponentModel;
using System.Globalization;
using Microsoft.Extensions.AI;
using Nebula.Application.Analytics;
using Nebula.Application.Common;
using Nebula.Application.Customers;
using Nebula.Application.Orders;
using Nebula.Application.Products;

namespace Nebula.Application.Ai;

/// <summary>
/// Read-only store data for the assistant, wrapping the existing feature services. Every result is deliberately
/// compact (at most <see cref="MaxRows"/> rows, only the fields an answer needs, dates without time) to keep the
/// tool messages, and so the token bill, small. Arguments come from the model and are parsed leniently.
/// Recorded answers call the same methods, so both modes quote identical numbers.
/// </summary>
public sealed class StoreDataTools(
    AnalyticsService analytics,
    OrdersService orders,
    CustomersService customers,
    ProductsService products)
{
    public const int MaxRows = 10;

    public const string SalesOverview = "get_sales_overview";
    public const string TopProducts = "get_top_products";
    public const string SalesByCategory = "get_sales_by_category";
    public const string ListOrders = "list_orders";
    public const string TopCustomers = "get_top_customers";
    public const string SearchProducts = "search_products";

    private const string RangeDescription = "Rolling window ending now: 7d, 30d (default), 90d or 12m.";

    /// <summary>The tools as <see cref="AIFunction"/>s for the chat model.</summary>
    public IList<AITool> CreateFunctions() =>
    [
        AIFunctionFactory.Create(
            GetSalesOverviewAsync,
            SalesOverview,
            "Revenue, order count, average order value and conversion for a period, each with the previous period of "
            + "equal length and the change in percent. Paid = not cancelled. Amounts in USD."),
        AIFunctionFactory.Create(
            GetTopProductsAsync,
            TopProducts,
            "Best-selling products by revenue in a period, with units sold."),
        AIFunctionFactory.Create(
            GetSalesByCategoryAsync,
            SalesByCategory,
            "Revenue per product category in a period, highest first."),
        AIFunctionFactory.Create(
            ListOrdersAsync,
            ListOrders,
            "Orders filtered by status with the total number of matches. Orders waiting to be shipped have status "
            + "new or packing."),
        AIFunctionFactory.Create(
            GetTopCustomersAsync,
            TopCustomers,
            "Customers with the highest lifetime value (sum of their paid orders)."),
        AIFunctionFactory.Create(
            SearchProductsAsync,
            SearchProducts,
            "Products whose name or SKU contains the query, with price, stock and units sold."),
    ];

    public async Task<SalesOverviewResult> GetSalesOverviewAsync(
        [Description(RangeDescription)] string? range = null,
        CancellationToken ct = default)
    {
        var (wire, parsed) = ParseRange(range);
        var kpis = (await analytics.OverviewAsync(parsed, ct)).ToDictionary(k => k.Key, StringComparer.Ordinal);
        return new SalesOverviewResult(
            wire,
            kpis["revenue"].Value,
            kpis["revenue"].Previous,
            kpis["revenue"].DeltaPct,
            (int)kpis["orders"].Value,
            (int)kpis["orders"].Previous,
            kpis["orders"].DeltaPct,
            kpis["aov"].Value,
            kpis["conversion"].Value);
    }

    public async Task<IReadOnlyList<TopProductRow>> GetTopProductsAsync(
        [Description(RangeDescription)] string? range = null,
        [Description("Number of products, 1-10 (default 5).")] int limit = 5,
        CancellationToken ct = default)
    {
        var top = await analytics.TopProductsAsync(ParseRange(range).Range, ClampLimit(limit), ct);
        return [.. top.Select(t => new TopProductRow(t.Product.Name, t.Product.Category.ToString(), t.UnitsSold, t.Revenue))];
    }

    public async Task<IReadOnlyList<CategorySalesRow>> GetSalesByCategoryAsync(
        [Description(RangeDescription)] string? range = null,
        CancellationToken ct = default)
    {
        var rows = await analytics.CategoriesAsync(ParseRange(range).Range, ct);
        return [.. rows.Select(r => new CategorySalesRow(r.Category.ToString(), r.Revenue))];
    }

    public async Task<OrderListResult> ListOrdersAsync(
        [Description("Comma-separated statuses: new, packing, shipped, delivered, cancelled. Empty = all.")]
        string? status = null,
        [Description("Number of orders to return, 1-10 (default 5).")] int limit = 5,
        [Description("true = oldest first (e.g. longest waiting), false = newest first.")] bool oldestFirst = false,
        CancellationToken ct = default)
    {
        var list = ListQuery.Parse("1", ClampLimit(limit).ToString(CultureInfo.InvariantCulture), "createdAt", oldestFirst ? "asc" : "desc", null);
        var page = await orders.ListAsync(OrderListQuery.Parse(list, status?.ToLowerInvariant(), null, null), ct);
        return new OrderListResult(
            page.Total,
            [.. page.Items.Select(o => new OrderRow(o.Number, o.CustomerName, o.Total, o.Status.ToWire(), Day(o.CreatedAt)))]);
    }

    public async Task<IReadOnlyList<CustomerRow>> GetTopCustomersAsync(
        [Description("Number of customers, 1-10 (default 5).")] int limit = 5,
        CancellationToken ct = default)
    {
        var list = ListQuery.Parse("1", ClampLimit(limit).ToString(CultureInfo.InvariantCulture), "lifetimeValue", "desc", null);
        var page = await customers.ListAsync(list, ct);
        return
        [
            .. page.Items.Select(c => new CustomerRow(
                c.Name, c.Country, c.OrdersCount, c.LifetimeValue, c.LastOrderAt is { } at ? Day(at) : null)),
        ];
    }

    public async Task<IReadOnlyList<ProductRow>> SearchProductsAsync(
        [Description("Part of a product name or SKU.")] string query,
        [Description("Number of products, 1-10 (default 5).")] int limit = 5,
        CancellationToken ct = default)
    {
        var list = ListQuery.Parse("1", ClampLimit(limit).ToString(CultureInfo.InvariantCulture), "sold", "desc", query);
        var page = await products.ListAsync(new ProductListQuery(list, null, StockFilter.All), ct);
        return
        [
            .. page.Items.Select(p => new ProductRow(p.Name, p.Sku, p.Category.ToString(), p.Price, p.Stock, p.Sold, p.Active)),
        ];
    }

    /// <summary>Unknown values mean the default <c>30d</c>: a model's slightly-off argument should not fail the tool.</summary>
    internal static (string Wire, RevenueRange Range) ParseRange(string? raw) => raw?.Trim().ToLowerInvariant() switch
    {
        "7d" => ("7d", RevenueRange.D7),
        "90d" => ("90d", RevenueRange.D90),
        "12m" => ("12m", RevenueRange.M12),
        _ => ("30d", RevenueRange.D30),
    };

    private static int ClampLimit(int limit) => Math.Clamp(limit, 1, MaxRows);

    private static string Day(DateTime value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

public sealed record SalesOverviewResult(
    string Range,
    decimal Revenue,
    decimal PreviousRevenue,
    double RevenueChangePct,
    int Orders,
    int PreviousOrders,
    double OrdersChangePct,
    decimal AverageOrderValue,
    decimal ConversionPct);

public sealed record TopProductRow(string Name, string Category, int UnitsSold, decimal Revenue);

public sealed record CategorySalesRow(string Category, decimal Revenue);

public sealed record OrderListResult(int Total, IReadOnlyList<OrderRow> Orders);

public sealed record OrderRow(int Number, string Customer, decimal Total, string Status, string CreatedAt);

public sealed record CustomerRow(string Name, string Country, int Orders, decimal LifetimeValue, string? LastOrderAt);

public sealed record ProductRow(string Name, string Sku, string Category, decimal Price, int Stock, int Sold, bool Active);
