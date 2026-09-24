using Nebula.Application.Common;
using Nebula.Application.Orders;
using Nebula.Domain;

namespace Nebula.Application.Customers;

/// <summary>Wire shape of the frontend <c>Customer</c> interface (<c>src/app/models/customer.ts</c>).</summary>
public sealed record CustomerDto(
    string Id,
    string Name,
    string Email,
    string AvatarUrl,
    string Phone,
    string Country,
    string CountryCode,
    DateTime CreatedAt,
    int OrdersCount,
    decimal LifetimeValue,
    DateTime? LastOrderAt,
    string Notes)
{
    public static CustomerDto From(Customer c)
    {
        ArgumentNullException.ThrowIfNull(c);
        return new CustomerDto(
            c.Id, c.Name, c.Email, c.AvatarUrl, c.Phone, c.Country, c.CountryCode, c.CreatedAt,
            c.OrdersCount, Money.Round2(c.LifetimeValue), c.LastOrderAt, c.Notes);
    }
}

/// <summary>Customer profile with all their orders, newest first.</summary>
public sealed record CustomerProfileDto(CustomerDto Customer, IReadOnlyList<OrderDto> Orders);
