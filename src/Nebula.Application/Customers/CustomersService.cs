using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Nebula.Application.Common;
using Nebula.Application.Orders;
using Nebula.Domain;

namespace Nebula.Application.Customers;

/// <summary>Port of <c>mock-api/handlers/customers.ts</c>.</summary>
public sealed class CustomersService(IAppDbContext db)
{
    public const string DefaultSort = "createdAt";

    /// <summary>Every scalar <c>Customer</c> property the mock can sort by.</summary>
    public static readonly IReadOnlyDictionary<string, Expression<Func<Customer, object?>>> Sortable =
        new Dictionary<string, Expression<Func<Customer, object?>>>(StringComparer.Ordinal)
        {
            ["id"] = c => c.Id,
            ["name"] = c => c.Name,
            ["email"] = c => c.Email,
            ["avatarUrl"] = c => c.AvatarUrl,
            ["phone"] = c => c.Phone,
            ["country"] = c => c.Country,
            ["countryCode"] = c => c.CountryCode,
            ["createdAt"] = c => c.CreatedAt,
            ["ordersCount"] = c => c.OrdersCount,
            ["lifetimeValue"] = c => c.LifetimeValue,
            ["lastOrderAt"] = c => c.LastOrderAt,
            ["notes"] = c => c.Notes,
        };

    public async Task<Paged<CustomerDto>> ListAsync(ListQuery q, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);

        var query = db.Customers.AsNoTracking();
        var term = q.Search?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(term))
        {
            query = query.Where(c =>
                c.Name.ToLower().Contains(term)
                || c.Email.ToLower().Contains(term)
                || c.Country.ToLower().Contains(term));
        }

        return await query
            .ApplySort(q.Sort, q.Dir, Sortable, DefaultSort, c => c.Id)
            .ToPagedAsync(q, CustomerDto.From, ct);
    }

    public async Task<CustomerProfileDto> GetAsync(string id, CancellationToken ct)
    {
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Customer");

        var orders = await db.Orders.AsNoTracking()
            .Where(o => o.CustomerId == id)
            .OrderByDescending(o => o.CreatedAt)
            .ThenByDescending(o => o.Number)
            .ToListAsync(ct);

        return new CustomerProfileDto(CustomerDto.From(customer), orders.Select(OrderMapping.ToDto).ToList());
    }
}
