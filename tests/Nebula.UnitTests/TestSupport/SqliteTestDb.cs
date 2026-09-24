using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Nebula.Application.Common;
using Nebula.Domain;
using Nebula.Infrastructure.Persistence;

namespace Nebula.UnitTests.TestSupport;

public sealed class FakeClock(DateTime now) : IClock
{
    public DateTime UtcNow { get; set; } = now;
}

/// <summary>File-backed SQLite database with a tiny hand-built data set: 3 customers, 3 products, 5 orders.</summary>
public sealed class SqliteTestDb : IAsyncDisposable
{
    public static readonly DateTime Now = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _path = Path.Combine(Path.GetTempPath(), "nebula-tests", $"unit-{Guid.NewGuid():N}.db");

    private SqliteTestDb()
    {
    }

    public static async Task<SqliteTestDb> CreateAsync(CancellationToken ct)
    {
        var db = new SqliteTestDb();
        Directory.CreateDirectory(Path.GetDirectoryName(db._path)!);
        await using var context = db.CreateContext();
        await context.Database.EnsureCreatedAsync(ct);
        Seed(context);
        await context.SaveChangesAsync(ct);
        return db;
    }

    public AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_path}", sqlite => sqlite.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)).Options);

    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            File.Delete(_path);
        }
        catch (IOException)
        {
        }

        return ValueTask.CompletedTask;
    }

    private static void Seed(AppDbContext db)
    {
        db.Products.AddRange(
            Product("prd_0001", "Cotton Tee", 20m, active: true, sold: 5),
            Product("prd_0002", "Canvas Sneaker", 80m, active: true, sold: 3),
            Product("prd_0003", "Old Lamp", 50m, active: false, sold: 1));

        db.Customers.AddRange(
            Customer("cus_0001", "Ann Lee", "ann@example.test"),
            Customer("cus_0002", "Bob Stone", "bob@shop.test"),
            Customer("cus_0003", "Cara Diaz", "cara@example.test"));

        db.Orders.AddRange(
            Order(1001, "cus_0001", "Ann Lee", "ann@example.test", OrderStatus.Delivered, Now.AddDays(-10), 40m),
            Order(1002, "cus_0002", "Bob Stone", "bob@shop.test", OrderStatus.Shipped, Now.AddDays(-5), 160m),
            Order(1003, "cus_0001", "Ann Lee", "ann@example.test", OrderStatus.New, Now.AddDays(-3), 20m),
            Order(1004, "cus_0003", "Cara Diaz", "cara@example.test", OrderStatus.Cancelled, Now.AddDays(-2), 80m),
            Order(1005, "cus_0002", "Bob Stone", "bob@shop.test", OrderStatus.Packing, Now.AddDays(-1), 100m));
    }

    private static Product Product(string id, string name, decimal price, bool active, int sold) => new()
    {
        Id = id,
        Sku = "SKU-" + id[^4..],
        Name = name,
        Category = ProductCategory.Apparel,
        Price = price,
        ImageUrl = $"https://img.test/{id}.jpg",
        Stock = 10,
        Sold = sold,
        Rating = 4.5,
        CreatedAt = Now.AddDays(-100),
        Active = active,
    };

    private static Customer Customer(string id, string name, string email) => new()
    {
        Id = id,
        Name = name,
        Email = email,
        AvatarUrl = $"https://img.test/{id}.png",
        Phone = "+1 555 0100",
        Country = "Germany",
        CountryCode = "DE",
        CreatedAt = Now.AddDays(-200),
    };

    private static Order Order(
        int number, string customerId, string name, string email, OrderStatus status, DateTime createdAt, decimal subtotal)
    {
        var shipping = subtotal >= 100 ? 0m : 7.99m;
        var tax = Math.Round(subtotal * 0.08m, 2);
        return new Order
        {
            Id = $"ord_{number - 1000:D6}",
            Number = number,
            CustomerId = customerId,
            CustomerName = name,
            CustomerEmail = email,
            CustomerAvatarUrl = $"https://img.test/{customerId}.png",
            Items = [new() { ProductId = "prd_0001", Name = "Cotton Tee", ImageUrl = "x", Sku = "SKU-0001", Quantity = 1, UnitPrice = subtotal }],
            Subtotal = subtotal,
            Shipping = shipping,
            Tax = tax,
            Total = subtotal + shipping + tax,
            Status = status,
            PaymentMethod = PaymentMethod.Card,
            CreatedAt = createdAt,
            ShippingAddress = new() { Line1 = $"{number} Main St", City = "Berlin", Country = "Germany", CountryCode = "DE", PostalCode = "10115" },
            History = [new() { Status = OrderStatus.New, At = createdAt, Note = "Order placed" }],
        };
    }
}
