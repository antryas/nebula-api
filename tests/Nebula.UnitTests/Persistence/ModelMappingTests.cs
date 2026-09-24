using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Nebula.Domain;
using Nebula.Infrastructure.Persistence;

namespace Nebula.UnitTests.Persistence;

public sealed class ModelMappingTests : IAsyncLifetime
{
    private static readonly DateTime Created = new(2026, 9, 20, 8, 15, 30, DateTimeKind.Utc);

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), "nebula-tests", $"unit-{Guid.NewGuid():N}.db");

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }

        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Order_round_trips_with_items_history_and_address()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var db = CreateContext())
        {
            db.Orders.Add(CreateOrder());
            await db.SaveChangesAsync(ct);
        }

        await using (var db = CreateContext())
        {
            var order = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == "ord_000001", ct);

            Assert.Equal(1001, order.Number);
            Assert.Equal(OrderStatus.Shipped, order.Status);
            Assert.Equal(PaymentMethod.ApplePay, order.PaymentMethod);
            Assert.Equal(129.97m, order.Subtotal);
            Assert.Equal(0m, order.Shipping);
            Assert.Equal(10.40m, order.Tax);
            Assert.Equal(140.37m, order.Total);
            Assert.Equal(Created, order.CreatedAt);
            Assert.Equal(DateTimeKind.Utc, order.CreatedAt.Kind);

            Assert.Equal(2, order.Items.Count);
            Assert.Equal(["prd_001", "prd_002"], order.Items.Select(i => i.ProductId));
            Assert.Equal(19.99m, order.Items[0].UnitPrice);
            Assert.Equal(3, order.Items[0].Quantity);
            Assert.Equal(70.00m, order.Items[1].UnitPrice);

            Assert.Equal(
                [OrderStatus.New, OrderStatus.Packing, OrderStatus.Shipped],
                order.History.Select(h => h.Status));
            Assert.All(order.History, h => Assert.Equal(DateTimeKind.Utc, h.At.Kind));
            Assert.Equal(Created.AddHours(30), order.History[2].At);
            Assert.Null(order.History[0].Note);
            Assert.Equal("Tracking 1Z999", order.History[2].Note);

            Assert.Equal("221B Baker Street", order.ShippingAddress.Line1);
            Assert.Equal("GB", order.ShippingAddress.CountryCode);
            Assert.Equal("NW1 6XE", order.ShippingAddress.PostalCode);
        }
    }

    [Fact]
    public async Task Product_round_trips_with_variants()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var db = CreateContext())
        {
            db.Products.Add(new Product
            {
                Id = "prd_001",
                Sku = "APP-0001",
                Name = "Cotton Tee",
                Description = "Soft tee",
                Category = ProductCategory.Apparel,
                Price = 24.99m,
                CompareAtPrice = 34.50m,
                ImageUrl = "https://example.test/tee.jpg",
                Stock = 12,
                Sold = 7,
                Rating = 4.6,
                CreatedAt = Created,
                Active = true,
                Variants =
                [
                    new() { Id = "var_001_1", Size = "S", Color = "Black", Stock = 5 },
                    new() { Id = "var_001_2", Size = "M", Color = "White", Stock = 7 },
                ],
            });
            await db.SaveChangesAsync(ct);
        }

        await using (var db = CreateContext())
        {
            var product = await db.Products.AsNoTracking().SingleAsync(p => p.Id == "prd_001", ct);

            Assert.Equal(ProductCategory.Apparel, product.Category);
            Assert.Equal(24.99m, product.Price);
            Assert.Equal(34.50m, product.CompareAtPrice);
            Assert.Equal(4.6, product.Rating);
            Assert.Equal(DateTimeKind.Utc, product.CreatedAt.Kind);
            Assert.Equal(["var_001_1", "var_001_2"], product.Variants.Select(v => v.Id));
            Assert.Equal("White", product.Variants[1].Color);
            Assert.Equal(7, product.Variants[1].Stock);
        }
    }

    [Fact]
    public async Task Decimals_sort_and_aggregate_in_sqlite()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var db = CreateContext())
        {
            db.Products.AddRange(
                new Product { Id = "a", Price = 100.5m, CreatedAt = Created },
                new Product { Id = "b", Price = 9.99m, CreatedAt = Created },
                new Product { Id = "c", Price = 20m, CreatedAt = Created });
            await db.SaveChangesAsync(ct);
        }

        await using (var db = CreateContext())
        {
            var ids = await db.Products.OrderBy(p => p.Price).Select(p => p.Id).ToListAsync(ct);
            var sum = await db.Products.SumAsync(p => p.Price, ct);

            Assert.Equal(["b", "c", "a"], ids);
            Assert.Equal(130.49m, Math.Round(sum, 2));
        }
    }

    private AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_dbPath}").Options);

    private static Order CreateOrder() => new()
    {
        Id = "ord_000001",
        Number = 1001,
        CustomerId = "cus_0001",
        CustomerName = "Ada Lovelace",
        CustomerEmail = "ada@example.test",
        CustomerAvatarUrl = "https://example.test/ada.png",
        Items =
        [
            new() { ProductId = "prd_001", Name = "Cotton Tee", ImageUrl = "https://example.test/tee.jpg", Sku = "APP-0001", Quantity = 3, UnitPrice = 19.99m },
            new() { ProductId = "prd_002", Name = "Canvas Tote", ImageUrl = "https://example.test/tote.jpg", Sku = "ACC-0002", Quantity = 1, UnitPrice = 70.00m },
        ],
        Subtotal = 129.97m,
        Shipping = 0m,
        Tax = 10.40m,
        Total = 140.37m,
        Status = OrderStatus.Shipped,
        PaymentMethod = PaymentMethod.ApplePay,
        CreatedAt = Created,
        ShippingAddress = new()
        {
            Line1 = "221B Baker Street",
            City = "London",
            Country = "United Kingdom",
            CountryCode = "GB",
            PostalCode = "NW1 6XE",
        },
        History =
        [
            new() { Status = OrderStatus.New, At = Created },
            new() { Status = OrderStatus.Packing, At = Created.AddHours(4) },
            new() { Status = OrderStatus.Shipped, At = Created.AddHours(30), Note = "Tracking 1Z999" },
        ],
    };
}
