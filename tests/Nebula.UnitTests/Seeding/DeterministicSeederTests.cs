using System.Text.Json;
using System.Text.RegularExpressions;
using Nebula.Application.Common;
using Nebula.Domain;
using Nebula.Infrastructure.Seeding;

namespace Nebula.UnitTests.Seeding;

public sealed class DeterministicSeederTests
{
    private static readonly DateTime Anchor = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);

    private static readonly Lazy<SeedData> Seed = new(() => DeterministicSeeder.Create(Anchor));

    [Fact]
    public void Same_anchor_produces_identical_data()
    {
        var first = JsonSerializer.Serialize(DeterministicSeeder.Create(Anchor));
        var second = JsonSerializer.Serialize(DeterministicSeeder.Create(Anchor));

        Assert.Equal(first, second);
    }

    [Fact]
    public void Data_follows_the_anchor()
    {
        var anchor = Anchor.AddDays(30).AddHours(5);

        var other = DeterministicSeeder.Create(anchor);

        Assert.True(other.Orders[^1].CreatedAt <= anchor);
        Assert.True(other.Orders[^1].CreatedAt > anchor.AddDays(-1), "latest order should be recent");
        Assert.True(other.Orders[0].CreatedAt >= anchor.AddDays(-395).Date);
    }

    [Fact]
    public void Counts_match_frontend_seed()
    {
        var seed = Seed.Value;

        Assert.Equal(60, seed.Products.Count);
        Assert.Equal(700, seed.Customers.Count);
        Assert.Equal(4800, seed.Orders.Count);
        Assert.Equal("usr_1", seed.User.Id);
        Assert.Equal("Alex Morgan", seed.User.Name);
        Assert.Equal("alex@nebula.store", seed.User.Email);
        Assert.Equal("Admin", seed.User.Role);
        Assert.All(Enum.GetValues<ProductCategory>(), c => Assert.Equal(10, seed.Products.Count(p => p.Category == c)));
    }

    [Fact]
    public void Orders_are_internally_consistent()
    {
        var seed = Seed.Value;
        var earliest = Anchor.AddDays(-395).Date;

        for (var i = 0; i < seed.Orders.Count; i++)
        {
            var o = seed.Orders[i];
            Assert.Equal(1001 + i, o.Number);
            Assert.InRange(o.Items.Count, 1, 4);
            Assert.Equal(o.Items.Count, o.Items.Select(it => it.ProductId).Distinct().Count());
            Assert.Equal(Money.Round2(o.Items.Sum(it => it.Quantity * it.UnitPrice)), o.Subtotal);
            Assert.Equal(o.Subtotal >= 100m ? 0m : 7.99m, o.Shipping);
            Assert.Equal(Money.Round2(o.Subtotal * 0.08m), o.Tax);
            Assert.Equal(Money.Round2(o.Subtotal + o.Shipping + o.Tax), o.Total);
            Assert.NotEmpty(o.History);
            Assert.Equal(OrderStatus.New, o.History[0].Status);
            Assert.Equal(o.CreatedAt, o.History[0].At);
            Assert.Equal(o.Status, o.History[^1].Status);
            Assert.True(o.CreatedAt <= Anchor, $"{o.Id} is in the future");
            Assert.True(o.CreatedAt >= earliest, $"{o.Id} is older than the history window");
            Assert.Equal(DateTimeKind.Utc, o.CreatedAt.Kind);
            Assert.All(o.History, h => Assert.InRange(h.At, o.CreatedAt, Anchor));
            if (i > 0)
            {
                Assert.True(seed.Orders[i - 1].CreatedAt <= o.CreatedAt, "orders must be chronological");
            }
        }

        // Status mix: old orders are delivered, a fulfillment backlog exists, a few are cancelled.
        var byStatus = seed.Orders.GroupBy(o => o.Status).ToDictionary(g => g.Key, g => g.Count());
        Assert.All(Enum.GetValues<OrderStatus>(), s => Assert.True(byStatus.GetValueOrDefault(s) > 0, $"no {s} orders"));
        Assert.All(
            seed.Orders.Where(o => o.CreatedAt < Anchor.AddDays(-4)),
            o => Assert.Contains(o.Status, new[] { OrderStatus.Delivered, OrderStatus.Cancelled }));
    }

    [Fact]
    public void Customer_aggregates_match_orders()
    {
        var seed = Seed.Value;
        var byCustomer = seed.Orders.ToLookup(o => o.CustomerId);

        foreach (var c in seed.Customers)
        {
            var own = byCustomer[c.Id].Where(o => o.Status != OrderStatus.Cancelled).ToList();
            Assert.Equal(own.Count, c.OrdersCount);
            Assert.Equal(Money.Round2(own.Sum(o => o.Total)), c.LifetimeValue);
            Assert.Equal(own.Count == 0 ? null : own.Max(o => o.CreatedAt), c.LastOrderAt);

            var first = byCustomer[c.Id].Select(o => (DateTime?)o.CreatedAt).Min();
            if (first is not null)
            {
                Assert.True(c.CreatedAt <= first, $"{c.Id} signed up after their first order");
            }
        }

        var sold = seed.Orders
            .Where(o => o.Status != OrderStatus.Cancelled)
            .SelectMany(o => o.Items)
            .GroupBy(it => it.ProductId)
            .ToDictionary(g => g.Key, g => g.Sum(it => it.Quantity));
        Assert.All(seed.Products, p =>
        {
            Assert.Equal(sold.GetValueOrDefault(p.Id), p.Sold);
            Assert.Equal(p.Variants.Sum(v => v.Stock), p.Stock);
        });
    }

    [Fact]
    public void Ids_are_unique_and_well_formed()
    {
        var seed = Seed.Value;

        AssertIds(seed.Products.Select(p => p.Id), @"^prd_\d{4}$");
        AssertIds(seed.Products.SelectMany(p => p.Variants).Select(v => v.Id), @"^prd_\d{4}_v\d+$");
        AssertIds(seed.Products.Select(p => p.Sku), @"^[A-Z]{3}-[A-Z]{3}-\d{3}$");
        AssertIds(seed.Customers.Select(c => c.Id), @"^cus_\d{4}$");
        AssertIds(seed.Orders.Select(o => o.Id), @"^ord_\d{6}$");
        Assert.Equal("prd_0001", seed.Products[0].Id);
        Assert.Equal("cus_0001", seed.Customers[0].Id);
        Assert.Equal("ord_000001", seed.Orders[0].Id);

        var productIds = seed.Products.Select(p => p.Id).ToHashSet();
        var customerIds = seed.Customers.Select(c => c.Id).ToHashSet();
        Assert.All(seed.Orders, o =>
        {
            Assert.Contains(o.CustomerId, customerIds);
            Assert.All(o.Items, it => Assert.Contains(it.ProductId, productIds));
        });
    }

    [Fact]
    public void Catalog_values_are_copied_from_the_frontend_seed()
    {
        var seed = Seed.Value;

        Assert.All(seed.Products, p =>
        {
            Assert.Matches(@"^https://picsum\.photos/id/\d+/400/400$", p.ImageUrl);
            Assert.InRange(p.Rating, 3.6, 5.0);
            Assert.True(p.Price > 0);
            Assert.True(p.CompareAtPrice is null || p.CompareAtPrice > p.Price);
        });
        Assert.Equal(60, seed.Products.Select(p => p.ImageUrl).Distinct().Count());
        Assert.All(seed.Customers, c =>
        {
            Assert.Equal($"https://i.pravatar.cc/80?u={c.Id}", c.AvatarUrl);
            Assert.Equal(c.Email.ToLowerInvariant(), c.Email);
        });
    }

    private static void AssertIds(IEnumerable<string> ids, string pattern)
    {
        var list = ids.ToList();
        Assert.Equal(list.Count, list.Distinct().Count());
        Assert.All(list, id => Assert.Matches(new Regex(pattern), id));
    }
}
