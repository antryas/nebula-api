using Microsoft.EntityFrameworkCore;
using Nebula.Application.Common;
using Nebula.Application.Customers;
using Nebula.Domain;

namespace Nebula.Application.Orders;

/// <summary>
/// Port of <c>createLiveOrder</c> (<c>mock-api/live-orders.ts</c>): a random existing customer checks out
/// 1-3 random active products right now; the order is stored as <c>new</c> and aggregates stay consistent.
/// Random draws happen in the same sequence as the mock.
/// </summary>
public sealed class LiveOrderFactory(IAppDbContext db, IClock clock, Random random)
{
    private const int NumberOffset = 1000;

    private static readonly PaymentMethod[] PaymentMethods =
        [PaymentMethod.Card, PaymentMethod.Card, PaymentMethod.PayPal, PaymentMethod.ApplePay];

    // Serializes number allocation process-wide; order numbers are unique.
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task<OrderDto> CreateAsync(CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            return (await CreateOrderAsync(ct)).ToDto();
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<Order> CreateOrderAsync(CancellationToken ct)
    {
        var customerCount = await db.Customers.CountAsync(ct);
        if (customerCount == 0)
        {
            throw new InvalidOperationException("There are no customers to place a live order.");
        }

        var customer = await db.Customers.AsNoTracking().OrderBy(c => c.Id).Skip(Pick(customerCount)).FirstAsync(ct);

        var products = await db.Products.OrderBy(p => p.Id).ToListAsync(ct);
        var active = products.Where(p => p.Active).ToList();
        var pool = active.Count > 0 ? active : products;
        var itemCount = Math.Min(pool.Count, 1 + (int)Math.Floor(random.NextDouble() * 3));

        var chosen = new List<int>(itemCount);
        while (chosen.Count < itemCount)
        {
            var index = Pick(pool.Count);
            if (!chosen.Contains(index))
            {
                chosen.Add(index);
            }
        }

        var items = chosen.Select(index =>
        {
            var p = pool[index];
            return new OrderItem
            {
                ProductId = p.Id,
                Name = p.Name,
                ImageUrl = p.ImageUrl,
                Sku = p.Sku,
                Quantity = random.NextDouble() < 0.8 ? 1 : 2,
                UnitPrice = p.Price,
            };
        }).ToList();

        var subtotal = Money.Round2(items.Sum(i => i.Quantity * i.UnitPrice));
        var shipping = subtotal >= 100 ? 0m : 7.99m;
        var tax = Money.Round2(subtotal * 0.08m);
        var createdAt = clock.UtcNow;
        var number = (await db.Orders.MaxAsync(o => (int?)o.Number, ct) ?? NumberOffset) + 1;

        var lastAddress = await db.Orders.AsNoTracking()
            .Where(o => o.CustomerId == customer.Id)
            .OrderByDescending(o => o.Number)
            .Select(o => o.ShippingAddress)
            .FirstOrDefaultAsync(ct);

        var order = new Order
        {
            Id = $"ord_{number - NumberOffset:D6}",
            Number = number,
            CustomerId = customer.Id,
            CustomerName = customer.Name,
            CustomerEmail = customer.Email,
            CustomerAvatarUrl = customer.AvatarUrl,
            Items = items,
            Subtotal = subtotal,
            Shipping = shipping,
            Tax = tax,
            Total = Money.Round2(subtotal + shipping + tax),
            Status = OrderStatus.New,
            PaymentMethod = PaymentMethods[Pick(PaymentMethods.Length)],
            CreatedAt = createdAt,
            ShippingAddress = lastAddress is null
                ? new Address
                {
                    Line1 = "1 Market Street",
                    City = customer.Country,
                    Country = customer.Country,
                    CountryCode = customer.CountryCode,
                    PostalCode = "00000",
                }
                : new Address
                {
                    Line1 = lastAddress.Line1,
                    City = lastAddress.City,
                    Country = lastAddress.Country,
                    CountryCode = lastAddress.CountryCode,
                    PostalCode = lastAddress.PostalCode,
                },
            History = [new StatusChange { Status = OrderStatus.New, At = createdAt, Note = "Order placed" }],
        };

        db.Orders.Add(order);
        foreach (var item in items)
        {
            pool.First(p => p.Id == item.ProductId).Sold += item.Quantity;
        }

        await db.SaveChangesAsync(ct);
        await CustomerAggregates.RecomputeAsync(db, customer.Id, ct);
        await db.SaveChangesAsync(ct);
        return order;
    }

    /// <summary>The mock's <c>pick</c>: <c>Math.floor(random() * length)</c>.</summary>
    private int Pick(int length) => (int)Math.Floor(random.NextDouble() * length);
}
