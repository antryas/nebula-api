using Microsoft.EntityFrameworkCore;
using Nebula.Domain;

namespace Nebula.Application.Common;

public interface IAppDbContext
{
    DbSet<Product> Products { get; }
    DbSet<Customer> Customers { get; }
    DbSet<Order> Orders { get; }
    DbSet<User> Users { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
