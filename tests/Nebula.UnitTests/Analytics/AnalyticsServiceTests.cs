using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Nebula.Application.Analytics;
using Nebula.Application.Common;
using Nebula.Domain;
using Nebula.Infrastructure.Persistence;

namespace Nebula.UnitTests.Analytics;

/// <summary>
/// Hand-built dataset, now = Thu 2026-09-24 12:00Z. The current 30d window starts 2026-08-25 12:00Z,
/// the previous one 2026-07-26 12:00Z.
/// </summary>
public sealed class AnalyticsServiceTests : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private AppDbContext _db = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private AnalyticsService Service => new(_db, new StubClock(Now));

    public async ValueTask InitializeAsync()
    {
        await _connection.OpenAsync(Ct);
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        await _db.Database.EnsureCreatedAsync(Ct);

        _db.Products.AddRange(
            Product("prd_001", ProductCategory.Apparel),
            Product("prd_002", ProductCategory.Electronics),
            Product("prd_003", ProductCategory.Home));

        _db.Orders.AddRange(
            // Monday 09:15 UTC, paid.
            Order(1, new DateTime(2026, 9, 21, 9, 15, 0, DateTimeKind.Utc), OrderStatus.Delivered, "DE", "Germany", ("prd_001", 2, 10m)),
            // Thursday 14:00 UTC, paid.
            Order(2, new DateTime(2026, 9, 10, 14, 0, 0, DateTimeKind.Utc), OrderStatus.New, "US", "United States", ("prd_002", 1, 100m)),
            // Cancelled orders never count.
            Order(3, new DateTime(2026, 9, 22, 18, 0, 0, DateTimeKind.Utc), OrderStatus.Cancelled, "US", "United States", ("prd_002", 5, 100m)),
            // Previous window and the oldest order: the previous 30d window is only half covered.
            Order(4, new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc), OrderStatus.Shipped, "FR", "France", ("prd_003", 1, 50m)));

        await _db.SaveChangesAsync(Ct);
        _db.ChangeTracker.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Overview_counts_paid_orders_and_extrapolates_partially_covered_previous_period()
    {
        var kpis = await Service.OverviewAsync(RevenueRange.D30, Ct);

        Assert.Equal(["revenue", "orders", "aov", "conversion"], kpis.Select(k => k.Key));
        Assert.Equal(["Revenue", "Orders", "Avg. order value", "Conversion rate"], kpis.Select(k => k.Label));
        Assert.Equal(["currency", "number", "currency", "percent"], kpis.Select(k => k.Format));

        var revenue = kpis[0];
        Assert.Equal(120m, revenue.Value);
        Assert.Equal(100m, revenue.Previous); // 50 x (1 / 0.5 coverage)
        Assert.Equal(20d, revenue.DeltaPct);
        Assert.Equal(12, revenue.Spark.Count);
        Assert.Equal(120m, revenue.Spark.Sum());

        var orders = kpis[1];
        Assert.Equal(2m, orders.Value);
        Assert.Equal(2m, orders.Previous);
        Assert.Equal(0d, orders.DeltaPct);

        var aov = kpis[2];
        Assert.Equal(60m, aov.Value);
        Assert.Equal(50m, aov.Previous); // ratios are not extrapolated

        // 2 / 70 synthetic visits; 70 is what the TS mock's visitsFor() yields for this dataset.
        Assert.Equal(2.86m, kpis[3].Value);
    }

    [Fact]
    public async Task Revenue_series_has_daily_buckets_labelled_by_their_end()
    {
        var points = await Service.RevenueAsync(RevenueRange.D30, Ct);

        Assert.Equal(30, points.Count);
        Assert.Equal("2026-08-26", points[0].Date);
        Assert.Equal("2026-09-24", points[^1].Date);
        Assert.Equal(new TimePointDto("2026-09-21", 20m, 1), points.Single(p => p.Date == "2026-09-21"));
        Assert.Equal(120m, points.Sum(p => p.Revenue));
        Assert.Equal(2, points.Sum(p => p.Orders));
    }

    [Fact]
    public async Task Categories_cover_all_categories_sorted_by_revenue_and_sum_to_revenue()
    {
        var categories = await Service.CategoriesAsync(RevenueRange.D30, Ct);

        Assert.Equal(
            [ProductCategory.Electronics, ProductCategory.Apparel, ProductCategory.Footwear,
             ProductCategory.Accessories, ProductCategory.Home, ProductCategory.Beauty],
            categories.Select(c => c.Category));
        Assert.Equal(100m, categories[0].Revenue);
        Assert.Equal(120m, categories.Sum(c => c.Revenue));
    }

    [Fact]
    public async Task Heatmap_has_168_cells_with_monday_zero_in_utc()
    {
        var cells = await Service.HeatmapAsync(RevenueRange.D30, Ct);

        Assert.Equal(7 * 24, cells.Count);
        Assert.Equal(new HeatCellDto(0, 9, 1), cells.Single(c => c is { Weekday: 0, Hour: 9 }));
        Assert.Equal(new HeatCellDto(3, 14, 1), cells.Single(c => c is { Weekday: 3, Hour: 14 }));
        Assert.Equal(2, cells.Sum(c => c.Orders));
    }

    [Fact]
    public async Task Geo_groups_by_shipping_country_sorted_by_revenue()
    {
        var geo = await Service.GeoAsync(RevenueRange.D30, Ct);

        Assert.Equal(
            [new GeoSalesDto("US", "United States", 100m, 1), new GeoSalesDto("DE", "Germany", 20m, 1)],
            geo);
    }

    [Fact]
    public async Task Funnel_applies_mock_drop_off_rates()
    {
        var funnel = await Service.FunnelAsync(RevenueRange.D30, Ct);

        Assert.Equal(["Visits", "Product views", "Added to cart", "Checkout", "Paid"], funnel.Select(s => s.Step));
        Assert.Equal(2, funnel[4].Value);
        Assert.Equal(3, funnel[3].Value); // round(2 / 0.64)
        Assert.Equal(7, funnel[2].Value); // round(3 / 0.42)
        Assert.Equal(32, funnel[1].Value); // round(70 * 0.46)
        Assert.Equal(70, funnel[0].Value); // TS mock reference value
        for (var i = 1; i < funnel.Count; i++)
        {
            Assert.True(funnel[i - 1].Value >= funnel[i].Value);
        }
    }

    [Fact]
    public async Task Top_products_are_ordered_by_revenue()
    {
        var top = await Service.TopProductsAsync(RevenueRange.D30, 5, Ct);

        Assert.Equal(["prd_002", "prd_001"], top.Select(t => t.Product.Id));
        Assert.Equal(1, top[0].UnitsSold);
        Assert.Equal(100m, top[0].Revenue);
        Assert.Equal(2, top[1].UnitsSold);
        Assert.Equal(20m, top[1].Revenue);
        Assert.Equal("prd_002_v1", Assert.Single(top[0].Product.Variants).Id);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(1, 1)]
    [InlineData(500, 2)]
    public async Task Top_products_limit_is_clamped(int limit, int expected)
    {
        var top = await Service.TopProductsAsync(RevenueRange.D30, limit, Ct);

        Assert.Equal(expected, top.Count);
    }

    [Fact]
    public async Task Orders_after_now_land_in_the_newest_bucket()
    {
        _db.Orders.Add(Order(5, Now.AddMinutes(30), OrderStatus.New, "DE", "Germany", ("prd_001", 1, 5m)));
        await _db.SaveChangesAsync(Ct);

        var points = await Service.RevenueAsync(RevenueRange.D7, Ct);

        Assert.Equal(new TimePointDto("2026-09-24", 5m, 1), points[^1]);
    }

    private static Product Product(string id, ProductCategory category) => new()
    {
        Id = id,
        Sku = id.ToUpperInvariant(),
        Name = $"Product {id}",
        Category = category,
        Price = 10m,
        Stock = 5,
        Active = true,
        CreatedAt = Now.AddYears(-1),
        Variants = [new ProductVariant { Id = $"{id}_v1", Size = "M", Color = "Black", Stock = 5 }],
    };

    private static Order Order(
        int n,
        DateTime at,
        OrderStatus status,
        string countryCode,
        string country,
        params (string ProductId, int Quantity, decimal UnitPrice)[] items)
    {
        var total = items.Sum(i => i.Quantity * i.UnitPrice);
        return new Order
        {
            Id = $"ord_{n:000000}",
            Number = 1000 + n,
            CustomerId = "cus_001",
            Status = status,
            CreatedAt = at,
            Subtotal = total,
            Total = total,
            ShippingAddress = new Address { Country = country, CountryCode = countryCode },
            Items = [.. items.Select(i => new OrderItem { ProductId = i.ProductId, Quantity = i.Quantity, UnitPrice = i.UnitPrice })],
        };
    }

    private sealed class StubClock(DateTime now) : IClock
    {
        public DateTime UtcNow { get; } = now;
    }
}
