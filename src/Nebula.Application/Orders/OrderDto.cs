using System.Text.Json.Serialization;
using Nebula.Application.Common;
using Nebula.Domain;

namespace Nebula.Application.Orders;

/// <summary>Wire shape of the frontend <c>Order</c> interface (<c>src/app/models/order.ts</c>).</summary>
public sealed record OrderDto(
    string Id,
    int Number,
    string CustomerId,
    string CustomerName,
    string CustomerEmail,
    string CustomerAvatarUrl,
    IReadOnlyList<OrderItemDto> Items,
    decimal Subtotal,
    decimal Shipping,
    decimal Tax,
    decimal Total,
    OrderStatus Status,
    PaymentMethod PaymentMethod,
    DateTime CreatedAt,
    AddressDto ShippingAddress,
    IReadOnlyList<StatusChangeDto> History);

public sealed record OrderItemDto(string ProductId, string Name, string ImageUrl, string Sku, int Quantity, decimal UnitPrice);

public sealed record AddressDto(string Line1, string City, string Country, string CountryCode, string PostalCode);

public sealed record StatusChangeDto(
    OrderStatus Status,
    DateTime At,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Note);

public static class OrderMapping
{
    public static OrderDto ToDto(this Order o)
    {
        ArgumentNullException.ThrowIfNull(o);
        return new OrderDto(
            o.Id,
            o.Number,
            o.CustomerId,
            o.CustomerName,
            o.CustomerEmail,
            o.CustomerAvatarUrl,
            o.Items.Select(i => new OrderItemDto(i.ProductId, i.Name, i.ImageUrl, i.Sku, i.Quantity, Money.Round2(i.UnitPrice))).ToList(),
            Money.Round2(o.Subtotal),
            Money.Round2(o.Shipping),
            Money.Round2(o.Tax),
            Money.Round2(o.Total),
            o.Status,
            o.PaymentMethod,
            o.CreatedAt,
            o.ShippingAddress.ToDto(),
            o.History.Select(h => new StatusChangeDto(h.Status, h.At, h.Note)).ToList());
    }

    public static AddressDto ToDto(this Address a)
    {
        ArgumentNullException.ThrowIfNull(a);
        return new AddressDto(a.Line1, a.City, a.Country, a.CountryCode, a.PostalCode);
    }
}
