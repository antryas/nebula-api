using Microsoft.EntityFrameworkCore;
using Nebula.Application.Common;
using Nebula.Application.Products;
using Nebula.Domain;

namespace Nebula.Application.Analytics;

/// <summary>
/// Dashboard analytics ported from <c>mock-api/handlers/analytics.ts</c>. "Paid" means every order that is not
/// cancelled. Each call projects only the columns it needs and aggregates in memory; all times are UTC.
/// </summary>
public sealed class AnalyticsService(IAppDbContext db, IClock clock)
{
    public const int DefaultTopProductsLimit = 5;
    public const int MaxTopProductsLimit = 50;

    private const int SparkBuckets = 12;

    /// <summary>Synthetic storefront traffic: visits per paid order today, and how fast conversion improves.</summary>
    private const double VisitsPerOrder = 38;
    private const double ConversionGainPerMonth = 0.03;

    private static readonly ProductCategory[] Categories =
    [
        ProductCategory.Apparel,
        ProductCategory.Footwear,
        ProductCategory.Accessories,
        ProductCategory.Electronics,
        ProductCategory.Home,
        ProductCategory.Beauty,
    ];

    private static readonly long UnixEpochTicks = DateTime.UnixEpoch.Ticks;

    public async Task<IReadOnlyList<KpiDto>> OverviewAsync(RevenueRange range, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var period = Periods.For(range, now);
        var orders = await PaidOrdersSinceAsync(period.PreviousStart, ct);
        var current = orders.Where(o => o.CreatedAt >= period.Start).ToList();
        var previous = orders.Where(o => o.CreatedAt < period.Start).ToList();

        // The seed only covers ~13 months, so the previous 12m window is mostly empty. Extrapolate additive
        // metrics (revenue, orders) from the part of that window that has data; ratios are unaffected by coverage.
        var coverage = PreviousCoverage(await FirstOrderAtAsync(ct), period.PreviousStart, period.Start);
        var extrapolate = coverage is > 0 and < 1 ? (decimal)(1 / coverage) : 1m;

        var sparkEdges = Periods.SplitEvenly(period.Start, period.End, SparkBuckets);
        var sparkBuckets = Periods.Bucketize(current, o => o.CreatedAt, sparkEdges);

        decimal Revenue(IReadOnlyCollection<PaidOrder> list) => Money.Round2(list.Sum(o => o.Total));
        decimal OrderCount(IReadOnlyCollection<PaidOrder> list) => list.Count;
        decimal Aov(IReadOnlyCollection<PaidOrder> list) => list.Count > 0 ? Money.Round2(Revenue(list) / list.Count) : 0m;
        decimal Conversion(IReadOnlyCollection<PaidOrder> list)
        {
            var visits = VisitsFor(list, now);
            return visits > 0 ? Money.Round2((decimal)list.Count / visits * 100) : 0m;
        }

        KpiDto Kpi(string key, string label, string format, Func<IReadOnlyCollection<PaidOrder>, decimal> metric, bool additive = false)
        {
            var value = metric(current);
            var scaled = metric(previous) * (additive ? extrapolate : 1m);
            var prev = format == "number" ? JsRound(scaled) : Money.Round2(scaled);
            return new KpiDto(
                key,
                label,
                value,
                prev,
                prev == 0 ? 0 : (double)Money.Round2((value - prev) / prev * 100),
                [.. sparkBuckets.Select(b => metric(b))],
                format);
        }

        return
        [
            Kpi("revenue", "Revenue", "currency", Revenue, additive: true),
            Kpi("orders", "Orders", "number", OrderCount, additive: true),
            Kpi("aov", "Avg. order value", "currency", Aov),
            Kpi("conversion", "Conversion rate", "percent", Conversion),
        ];
    }

    public async Task<IReadOnlyList<TimePointDto>> RevenueAsync(RevenueRange range, CancellationToken ct)
    {
        var period = Periods.For(range, clock.UtcNow);
        var buckets = Periods.Bucketize(await PaidOrdersSinceAsync(period.Start, ct), o => o.CreatedAt, period.Edges);
        return
        [
            .. buckets.Select((list, i) => new TimePointDto(
                Periods.BucketLabel(range, period.Edges[i + 1]),
                Money.Round2(list.Sum(o => o.Total)),
                list.Count)),
        ];
    }

    public async Task<IReadOnlyList<CategorySalesDto>> CategoriesAsync(RevenueRange range, CancellationToken ct)
    {
        var categoryOf = await db.Products.AsNoTracking()
            .Select(p => new { p.Id, p.Category })
            .ToDictionaryAsync(p => p.Id, p => p.Category, ct);
        var totals = Categories.ToDictionary(c => c, _ => 0m);
        foreach (var item in await PaidItemsSinceAsync(Periods.For(range, clock.UtcNow).Start, ct))
        {
            if (categoryOf.TryGetValue(item.ProductId, out var category))
            {
                totals[category] += item.Quantity * item.UnitPrice;
            }
        }

        return
        [
            .. Categories
                .Select(c => new CategorySalesDto(c, Money.Round2(totals[c])))
                .OrderByDescending(c => c.Revenue),
        ];
    }

    /// <summary>Orders per weekday/hour in UTC, like the mock (<c>getUTCDay</c>/<c>getUTCHours</c>).</summary>
    public async Task<IReadOnlyList<HeatCellDto>> HeatmapAsync(RevenueRange range, CancellationToken ct)
    {
        var counts = new int[7 * 24];
        foreach (var order in await PaidOrdersSinceAsync(Periods.For(range, clock.UtcNow).Start, ct))
        {
            var weekday = ((int)order.CreatedAt.DayOfWeek + 6) % 7; // 0 = Monday
            counts[(weekday * 24) + order.CreatedAt.Hour]++;
        }

        return [.. counts.Select((orders, i) => new HeatCellDto(i / 24, i % 24, orders))];
    }

    public async Task<IReadOnlyList<GeoSalesDto>> GeoAsync(RevenueRange range, CancellationToken ct)
    {
        var from = Periods.For(range, clock.UtcNow).Start;
        var orders = await PaidOrders(from)
            .Select(o => new { o.ShippingAddress.CountryCode, o.ShippingAddress.Country, o.Total })
            .ToListAsync(ct);

        var rows = new List<(string CountryCode, string Country, decimal Revenue, int Orders)>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var order in orders)
        {
            if (index.TryGetValue(order.CountryCode, out var i))
            {
                var row = rows[i];
                rows[i] = row with { Revenue = row.Revenue + order.Total, Orders = row.Orders + 1 };
            }
            else
            {
                index[order.CountryCode] = rows.Count;
                rows.Add((order.CountryCode, order.Country, order.Total, 1));
            }
        }

        return
        [
            .. rows
                .Select(r => new GeoSalesDto(r.CountryCode, r.Country, Money.Round2(r.Revenue), r.Orders))
                .OrderByDescending(r => r.Revenue),
        ];
    }

    public async Task<IReadOnlyList<FunnelStepDto>> FunnelAsync(RevenueRange range, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var orders = await PaidOrdersSinceAsync(Periods.For(range, now).Start, ct);
        long paid = orders.Count;
        var visits = VisitsFor(orders, now);

        // Typical e-commerce drop-off: ~64% of checkouts pay, ~42% of carts reach checkout.
        var checkout = Math.Max(paid, JsRound(paid / 0.64));
        var cart = Math.Max(checkout, JsRound(checkout / 0.42));
        var views = Math.Min(visits, Math.Max(cart, JsRound(visits * 0.46)));
        return
        [
            new FunnelStepDto("Visits", Math.Max(visits, views)),
            new FunnelStepDto("Product views", views),
            new FunnelStepDto("Added to cart", cart),
            new FunnelStepDto("Checkout", checkout),
            new FunnelStepDto("Paid", paid),
        ];
    }

    /// <summary>Best sellers by revenue; <paramref name="limit"/> is clamped to 1..50.</summary>
    public async Task<IReadOnlyList<TopProductDto>> TopProductsAsync(RevenueRange range, int limit, CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, MaxTopProductsLimit);

        var stats = new List<(string ProductId, int UnitsSold, decimal Revenue)>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in await PaidItemsSinceAsync(Periods.For(range, clock.UtcNow).Start, ct))
        {
            var amount = item.Quantity * item.UnitPrice;
            if (index.TryGetValue(item.ProductId, out var i))
            {
                var row = stats[i];
                stats[i] = row with { UnitsSold = row.UnitsSold + item.Quantity, Revenue = row.Revenue + amount };
            }
            else
            {
                index[item.ProductId] = stats.Count;
                stats.Add((item.ProductId, item.Quantity, amount));
            }
        }

        var ids = stats.Select(s => s.ProductId).ToList();
        var products = await db.Products.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        return
        [
            .. stats
                .Where(s => products.ContainsKey(s.ProductId))
                .Select(s => new TopProductDto(products[s.ProductId].ToDto(), s.UnitsSold, Money.Round2(s.Revenue)))
                .OrderByDescending(t => t.Revenue)
                .Take(limit),
        ];
    }

    // -----------------------------------------------------------------------------------------
    // Queries

    private IQueryable<Order> PaidOrders(DateTime from) =>
        db.Orders.AsNoTracking()
            .Where(o => o.Status != OrderStatus.Cancelled && o.CreatedAt >= from)
            .OrderBy(o => o.Number);

    private Task<List<PaidOrder>> PaidOrdersSinceAsync(DateTime from, CancellationToken ct) =>
        PaidOrders(from).Select(o => new PaidOrder(o.CreatedAt, o.Total)).ToListAsync(ct);

    private Task<List<PaidItem>> PaidItemsSinceAsync(DateTime from, CancellationToken ct) =>
        PaidOrders(from)
            .SelectMany(o => o.Items)
            .Select(i => new PaidItem(i.ProductId, i.Quantity, i.UnitPrice))
            .ToListAsync(ct);

    /// <summary>Oldest order of any status, or null when there are none.</summary>
    private Task<DateTime?> FirstOrderAtAsync(CancellationToken ct) =>
        db.Orders.AsNoTracking()
            .OrderBy(o => o.CreatedAt)
            .Select(o => (DateTime?)o.CreatedAt)
            .FirstOrDefaultAsync(ct);

    // -----------------------------------------------------------------------------------------
    // Helpers

    /// <summary>Share of <c>[from, to)</c> that lies after the first recorded order (0..1).</summary>
    private static double PreviousCoverage(DateTime? first, DateTime from, DateTime to)
    {
        if (first is null || first.Value >= to)
        {
            return 0;
        }

        var start = first.Value > from ? first.Value : from;
        return (double)(to.Ticks - start.Ticks) / (to.Ticks - from.Ticks);
    }

    /// <summary>
    /// Synthetic visits behind a set of paid orders: ~38 visits per order with deterministic day-level noise
    /// (±15%), and slightly more visits per order further in the past so conversion trends gently upward.
    /// </summary>
    private static long VisitsFor(IEnumerable<PaidOrder> orders, DateTime now)
    {
        const double dayMs = 86_400_000;
        var nowMs = UnixMs(now);
        var total = 0d;
        foreach (var order in orders)
        {
            var at = UnixMs(order.CreatedAt);
            var day = (int)Math.Floor(at / dayMs);
            var monthsAgo = Math.Max(0, (nowMs - at) / (30.4 * dayMs));
            total += VisitsPerOrder * (1 + (ConversionGainPerMonth * monthsAgo)) * DayNoise(day);
        }

        return JsRound(total);
    }

    /// <summary>Deterministic pseudo-random factor in [0.85, 1.15] for a day number (bit-exact port of the mock).</summary>
    private static double DayNoise(int day)
    {
        unchecked
        {
            var h = (day ^ 0x5bd1e995) * 0x2c1b3c6d;
            h = (h ^ (int)((uint)h >> 15)) * 0x297a2d39;
            h ^= (int)((uint)h >> 13);
            return 0.85 + ((uint)h / (double)0xffffffff * 0.3);
        }
    }

    private static double UnixMs(DateTime value) => (value.Ticks - UnixEpochTicks) / TimeSpan.TicksPerMillisecond;

    /// <summary>JavaScript <c>Math.round</c>: halves round towards +∞.</summary>
    private static long JsRound(double value) => (long)Math.Floor(value + 0.5);

    private static decimal JsRound(decimal value) => Math.Floor(value + 0.5m);

    private sealed record PaidOrder(DateTime CreatedAt, decimal Total);

    private sealed record PaidItem(string ProductId, int Quantity, decimal UnitPrice);
}
