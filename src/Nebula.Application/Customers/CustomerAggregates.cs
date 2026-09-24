using Microsoft.EntityFrameworkCore;
using Nebula.Application.Common;
using Nebula.Domain;

namespace Nebula.Application.Customers;

/// <summary>Port of the mock's <c>recomputeCustomer</c>.</summary>
public static class CustomerAggregates
{
    /// <summary>
    /// Recalculates <c>ordersCount</c> / <c>lifetimeValue</c> / <c>lastOrderAt</c> from the customer's
    /// non-cancelled orders as stored in the database (save pending order changes first). The customer is
    /// updated on the tracked entity; the caller saves.
    /// </summary>
    public static async Task RecomputeAsync(IAppDbContext db, string customerId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);

        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == customerId, ct);
        if (customer is null)
        {
            return;
        }

        var own = await db.Orders.AsNoTracking()
            .Where(o => o.CustomerId == customerId && o.Status != OrderStatus.Cancelled)
            .Select(o => new { o.Total, o.CreatedAt })
            .ToListAsync(ct);

        customer.OrdersCount = own.Count;
        customer.LifetimeValue = Money.Round2(own.Sum(o => o.Total));
        customer.LastOrderAt = own.Count == 0 ? null : own.Max(o => o.CreatedAt);
    }
}
