using Microsoft.EntityFrameworkCore;
using Nebula.Application.Common;
using Nebula.Domain;
using Nebula.Infrastructure.Persistence.Converters;

namespace Nebula.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<User> Users => Set<User>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite has no decimal type: store as REAL so ORDER BY / SUM work server-side.
        configurationBuilder.Properties<decimal>().HaveConversion<double>();
        configurationBuilder.Properties<decimal?>().HaveConversion<double?>();

        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();

        configurationBuilder.Properties<OrderStatus>().HaveConversion<string>().HaveMaxLength(16);
        configurationBuilder.Properties<PaymentMethod>().HaveConversion<string>().HaveMaxLength(16);
        configurationBuilder.Properties<ProductCategory>().HaveConversion<string>().HaveMaxLength(16);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}
