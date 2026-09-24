using Microsoft.EntityFrameworkCore;
using Nebula.Application.Orders;
using Nebula.Domain;
using Nebula.UnitTests.TestSupport;

namespace Nebula.UnitTests.Orders;

public sealed class LiveOrderFactoryTests : IAsyncLifetime
{
    private SqliteTestDb _db = null!;

    public async ValueTask InitializeAsync() => _db = await SqliteTestDb.CreateAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    [Fact]
    public async Task Creates_a_consistent_new_order()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = SqliteTestDb.Now.AddMinutes(1);
        Dictionary<string, int> soldBefore;
        Dictionary<string, int> countBefore;
        await using (var context = _db.CreateContext())
        {
            soldBefore = await context.Products.ToDictionaryAsync(p => p.Id, p => p.Sold, ct);
            countBefore = (await context.Orders.Where(o => o.Status != OrderStatus.Cancelled).Select(o => o.CustomerId).ToListAsync(ct))
                .CountBy(id => id).ToDictionary();
        }

        OrderDto dto;
        await using (var context = _db.CreateContext())
        {
            dto = await new LiveOrderFactory(context, new FakeClock(now), new Random(1)).CreateAsync(ct);
        }

        Assert.Equal(1006, dto.Number);
        Assert.Equal("ord_000006", dto.Id);
        Assert.Equal(OrderStatus.New, dto.Status);
        Assert.Equal(now, dto.CreatedAt);
        var placed = Assert.Single(dto.History);
        Assert.Equal("Order placed", placed.Note);
        Assert.Equal(now, placed.At);
        Assert.InRange(dto.Items.Count, 1, 2); // only two active products exist
        Assert.DoesNotContain(dto.Items, i => i.ProductId == "prd_0003");
        Assert.All(dto.Items, i => Assert.InRange(i.Quantity, 1, 2));
        Assert.Equal(dto.Items.Sum(i => i.Quantity * i.UnitPrice), dto.Subtotal);
        Assert.Equal(dto.Subtotal >= 100 ? 0m : 7.99m, dto.Shipping);
        Assert.Equal(Math.Round(dto.Subtotal * 0.08m, 2), dto.Tax);
        Assert.Equal(dto.Subtotal + dto.Shipping + dto.Tax, dto.Total);

        await using (var context = _db.CreateContext())
        {
            var stored = await context.Orders.AsNoTracking().SingleAsync(o => o.Id == dto.Id, ct);
            Assert.Equal(dto.Total, stored.Total);
            Assert.Equal(OrderStatus.New, stored.Status);

            var sold = await context.Products.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Sold, ct);
            foreach (var item in dto.Items)
            {
                Assert.Equal(soldBefore[item.ProductId] + item.Quantity, sold[item.ProductId]);
            }

            var customer = await context.Customers.AsNoTracking().SingleAsync(c => c.Id == dto.CustomerId, ct);
            Assert.Equal(countBefore.GetValueOrDefault(dto.CustomerId) + 1, customer.OrdersCount);
            Assert.Equal(now, customer.LastOrderAt);
            Assert.Equal(dto.CustomerName, customer.Name);
        }
    }

    [Fact]
    public async Task Numbers_increase_and_the_last_address_is_reused()
    {
        var ct = TestContext.Current.CancellationToken;

        for (var i = 0; i < 6; i++)
        {
            await using var context = _db.CreateContext();
            var dto = await new LiveOrderFactory(context, new FakeClock(SqliteTestDb.Now), new Random(i)).CreateAsync(ct);

            Assert.Equal(1006 + i, dto.Number);
            var expected = await context.Orders.AsNoTracking()
                .Where(o => o.CustomerId == dto.CustomerId && o.Number < dto.Number)
                .OrderByDescending(o => o.Number)
                .Select(o => o.ShippingAddress.Line1)
                .FirstAsync(ct);
            Assert.Equal(expected, dto.ShippingAddress.Line1);
        }
    }
}
