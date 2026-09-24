using Nebula.Domain;

namespace Nebula.Application.Orders;

public sealed record UpdateStatusRequest(string? Status);

public sealed record BulkStatusRequest(IReadOnlyList<string>? Ids, string? Status);

/// <summary>Same body as the mock: <c>{ updated }</c>.</summary>
public sealed record BulkStatusResult(int Updated);

/// <summary>Order status wire values, exactly the frontend <c>OrderStatus</c> union.</summary>
public static class OrderStatuses
{
    private static readonly Dictionary<string, OrderStatus> ByWireName = new(StringComparer.Ordinal)
    {
        ["new"] = OrderStatus.New,
        ["packing"] = OrderStatus.Packing,
        ["shipped"] = OrderStatus.Shipped,
        ["delivered"] = OrderStatus.Delivered,
        ["cancelled"] = OrderStatus.Cancelled,
    };

    /// <summary>Case-sensitive, like the mock's <c>isStatus</c>.</summary>
    public static bool TryParse(string? value, out OrderStatus status) =>
        ByWireName.TryGetValue(value ?? "", out status);

    public static string ToWire(this OrderStatus status) => status switch
    {
        OrderStatus.New => "new",
        OrderStatus.Packing => "packing",
        OrderStatus.Shipped => "shipped",
        OrderStatus.Delivered => "delivered",
        OrderStatus.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    /// <summary>Delivered and cancelled orders can no longer change status.</summary>
    public static bool IsClosed(this OrderStatus status) => status is OrderStatus.Delivered or OrderStatus.Cancelled;
}
