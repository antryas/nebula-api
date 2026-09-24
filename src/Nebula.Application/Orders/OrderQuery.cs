using System.Globalization;
using Nebula.Application.Common;
using Nebula.Domain;

namespace Nebula.Application.Orders;

/// <summary>
/// Parsed <c>GET /api/orders</c> query. <paramref name="Statuses"/> is null when no <c>status</c> filter was sent;
/// an empty list means a filter of only unknown values, which (like the mock) matches nothing.
/// </summary>
public sealed record OrderListQuery(ListQuery List, IReadOnlyList<OrderStatus>? Statuses, DateTime? From, DateTime? To)
{
    public static OrderListQuery Parse(ListQuery list, string? status, string? from, string? to)
    {
        var raw = (status ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        IReadOnlyList<OrderStatus>? statuses = raw.Length == 0
            ? null
            : raw.Select(s => OrderStatuses.TryParse(s, out var parsed) ? parsed : (OrderStatus?)null)
                .OfType<OrderStatus>()
                .Distinct()
                .ToList();
        return new OrderListQuery(list, statuses, ParseTime(from), ParseTime(to));
    }

    /// <summary>Like the mock's <c>parseTime</c>: missing or unparseable ⇒ no bound. Times without an offset are UTC.</summary>
    internal static DateTime? ParseTime(string? raw) =>
        !string.IsNullOrWhiteSpace(raw)
        && DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.UtcDateTime
            : null;
}
