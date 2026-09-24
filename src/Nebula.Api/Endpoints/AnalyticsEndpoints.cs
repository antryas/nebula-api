using System.Globalization;
using Nebula.Application.Analytics;

namespace Nebula.Api.Endpoints;

public static class AnalyticsEndpoints
{
    private const string RangeDescription =
        "Rolling window ending now: `7d`, `30d` (default), `90d` or `12m`. Anything else is 422 `validation`. " +
        "\"Paid\" orders are all orders that are not cancelled.";

    public static RouteGroupBuilder MapAnalyticsEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var group = api.MapGroup("/analytics")
            .WithTags("Analytics")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/overview", (string? range, AnalyticsService service, CancellationToken ct) =>
                service.OverviewAsync(RevenueRangeParser.Parse(range), ct))
            .WithName("GetAnalyticsOverview")
            .WithSummary("KPI tiles")
            .WithDescription($"Revenue, orders, average order value and conversion with previous-period deltas and 12-point sparklines. {RangeDescription}");

        group.MapGet("/revenue", (string? range, AnalyticsService service, CancellationToken ct) =>
                service.RevenueAsync(RevenueRangeParser.Parse(range), ct))
            .WithName("GetAnalyticsRevenue")
            .WithSummary("Revenue time series")
            .WithDescription($"Daily buckets (monthly for `12m`) labelled by the day/month they end on. {RangeDescription}");

        group.MapGet("/categories", (string? range, AnalyticsService service, CancellationToken ct) =>
                service.CategoriesAsync(RevenueRangeParser.Parse(range), ct))
            .WithName("GetAnalyticsCategories")
            .WithSummary("Sales by category")
            .WithDescription($"Line-item revenue per product category, highest first. {RangeDescription}");

        group.MapGet("/heatmap", (string? range, AnalyticsService service, CancellationToken ct) =>
                service.HeatmapAsync(RevenueRangeParser.Parse(range), ct))
            .WithName("GetAnalyticsHeatmap")
            .WithSummary("Orders by weekday and hour")
            .WithDescription($"168 cells in UTC; weekday 0 = Monday. {RangeDescription}");

        group.MapGet("/geo", (string? range, AnalyticsService service, CancellationToken ct) =>
                service.GeoAsync(RevenueRangeParser.Parse(range), ct))
            .WithName("GetAnalyticsGeo")
            .WithSummary("Sales by shipping country")
            .WithDescription($"Revenue and order count per shipping country, highest revenue first. {RangeDescription}");

        group.MapGet("/funnel", (string? range, AnalyticsService service, CancellationToken ct) =>
                service.FunnelAsync(RevenueRangeParser.Parse(range), ct))
            .WithName("GetAnalyticsFunnel")
            .WithSummary("Conversion funnel")
            .WithDescription($"Visits → product views → cart → checkout → paid, derived from paid orders with synthetic traffic. {RangeDescription}");

        group.MapGet("/top-products", (string? range, string? limit, AnalyticsService service, CancellationToken ct) =>
                service.TopProductsAsync(RevenueRangeParser.Parse(range), ParseLimit(limit), ct))
            .WithName("GetAnalyticsTopProducts")
            .WithSummary("Best-selling products")
            .WithDescription($"Products by line-item revenue. `limit` is clamped to 1..50 (default 5; non-numeric ⇒ 5). {RangeDescription}");

        return api;
    }

    /// <summary>Mirrors the mock's <c>Number.parseInt(limit ?? '5', 10)</c>: leading integer, otherwise the default.</summary>
    private static int ParseLimit(string? raw)
    {
        var text = (raw ?? "").TrimStart();
        var length = 0;
        if (length < text.Length && text[length] is '+' or '-')
        {
            length++;
        }

        while (length < text.Length && char.IsAsciiDigit(text[length]))
        {
            length++;
        }

        var digits = text.AsSpan(0, length);
        if (!digits.ContainsAnyInRange('0', '9'))
        {
            return AnalyticsService.DefaultTopProductsLimit;
        }

        // Huge values only need to survive the 1..50 clamp.
        return int.TryParse(digits, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
            ? value
            : digits[0] == '-' ? int.MinValue : int.MaxValue;
    }
}
