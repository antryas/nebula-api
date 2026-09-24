using Nebula.Application.Products;
using Nebula.Domain;

namespace Nebula.Application.Analytics;

/// <summary>Dashboard KPI tile. <c>Key</c>: revenue | orders | aov | conversion; <c>Format</c>: currency | number | percent.</summary>
public sealed record KpiDto(
    string Key,
    string Label,
    decimal Value,
    decimal Previous,
    double DeltaPct,
    IReadOnlyList<decimal> Spark,
    string Format);

/// <summary>Revenue bucket; <c>Date</c> is <c>yyyy-MM-dd</c> (monthly buckets use the 1st).</summary>
public sealed record TimePointDto(string Date, decimal Revenue, int Orders);

public sealed record CategorySalesDto(ProductCategory Category, decimal Revenue);

/// <summary>Orders per UTC weekday/hour; weekday 0 = Monday.</summary>
public sealed record HeatCellDto(int Weekday, int Hour, int Orders);

public sealed record GeoSalesDto(string CountryCode, string Country, decimal Revenue, int Orders);

/// <summary><c>Step</c>: Visits | Product views | Added to cart | Checkout | Paid.</summary>
public sealed record FunnelStepDto(string Step, long Value);

public sealed record TopProductDto(ProductDto Product, int UnitsSold, decimal Revenue);
