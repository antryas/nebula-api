using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Nebula.Application.Common;
using Nebula.Application.Customers;
using Nebula.Domain;

namespace Nebula.Application.Orders;

/// <summary>Port of <c>mock-api/handlers/orders.ts</c>. Filtering, sorting and paging run in SQL.</summary>
public sealed class OrdersService(IAppDbContext db, IClock clock)
{
    public const string DefaultSort = "createdAt";

    /// <summary>Every scalar <c>Order</c> property the mock can sort by.</summary>
    public static readonly IReadOnlyDictionary<string, Expression<Func<Order, object?>>> Sortable =
        new Dictionary<string, Expression<Func<Order, object?>>>(StringComparer.Ordinal)
        {
            ["id"] = o => o.Id,
            ["number"] = o => o.Number,
            ["customerId"] = o => o.CustomerId,
            ["customerName"] = o => o.CustomerName,
            ["customerEmail"] = o => o.CustomerEmail,
            ["customerAvatarUrl"] = o => o.CustomerAvatarUrl,
            ["subtotal"] = o => o.Subtotal,
            ["shipping"] = o => o.Shipping,
            ["tax"] = o => o.Tax,
            ["total"] = o => o.Total,
            ["status"] = o => o.Status,
            ["paymentMethod"] = o => o.PaymentMethod,
            ["createdAt"] = o => o.CreatedAt,
        };

    public async Task<Paged<OrderDto>> ListAsync(OrderListQuery q, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);

        var query = db.Orders.AsNoTracking();

        if (q.Statuses is { } statuses)
        {
            query = query.Where(o => statuses.Contains(o.Status));
        }

        if (q.From is { } from)
        {
            query = query.Where(o => o.CreatedAt >= from);
        }

        if (q.To is { } to)
        {
            query = query.Where(o => o.CreatedAt <= to);
        }

        var term = q.List.Search?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(term))
        {
            query = query.Where(o =>
                o.Number.ToString().Contains(term)
                || o.CustomerName.ToLower().Contains(term)
                || o.CustomerEmail.ToLower().Contains(term));
        }

        return await query
            .ApplySort(q.List.Sort, q.List.Dir, Sortable, DefaultSort, o => o.Number)
            
            .ToPagedAsync(q.List, OrderMapping.ToDto, ct);
    }

    public async Task<OrderDto> GetAsync(string id, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct)
            ?? throw new NotFoundException("Order");
        return order.ToDto();
    }

    public async Task<OrderDto> UpdateStatusAsync(string id, UpdateStatusRequest? r, CancellationToken ct)
    {
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == id, ct)
            ?? throw new NotFoundException("Order");

        if (!OrderStatuses.TryParse(r?.Status, out var status))
        {
            throw new ValidationFailedException(
                "Unknown order status", new Dictionary<string, string> { ["status"] = "Unknown order status" });
        }

        if (order.Status.IsClosed())
        {
            throw new ApiException(
                422,
                "invalid_transition",
                $"Order #{order.Number} is {order.Status.ToWire()} and can no longer change status");
        }

        await ChangeStatusAsync([order], status, ct);
        return order.ToDto();
    }

    public async Task<BulkStatusResult> BulkUpdateStatusAsync(BulkStatusRequest? r, CancellationToken ct)
    {
        if (r?.Ids is not { } ids || !OrderStatuses.TryParse(r.Status, out var status))
        {
            throw new ValidationFailedException("Expected { ids: string[]; status }");
        }

        var wanted = ids.Distinct(StringComparer.Ordinal).ToList();
        var orders = await db.Orders
            .Where(o => wanted.Contains(o.Id)
                && o.Status != OrderStatus.Delivered
                && o.Status != OrderStatus.Cancelled)
            .OrderBy(o => o.Number)
            .ToListAsync(ct);

        await ChangeStatusAsync(orders, status, ct);
        // Like the mock, orders already in the target status count as updated.
        return new BulkStatusResult(orders.Count);
    }

    /// <summary>
    /// Port of the mock's <c>changeStatus</c> for tracked orders: records history, and on cancellation gives the
    /// units back (<c>sold</c>, floored at 0) and recomputes the customers' aggregates.
    /// </summary>
    private async Task ChangeStatusAsync(IReadOnlyList<Order> orders, OrderStatus status, CancellationToken ct)
    {
        var changed = orders.Where(o => o.Status != status).ToList();
        if (changed.Count == 0)
        {
            return;
        }

        var at = clock.UtcNow;
        foreach (var order in changed)
        {
            order.Status = status;
            order.History.Add(new StatusChange { Status = status, At = at });
        }

        if (status != OrderStatus.Cancelled)
        {
            await db.SaveChangesAsync(ct);
            return;
        }

        var productIds = changed.SelectMany(o => o.Items).Select(i => i.ProductId).Distinct().ToList();
        var products = await db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        foreach (var item in changed.SelectMany(o => o.Items))
        {
            if (products.TryGetValue(item.ProductId, out var product))
            {
                product.Sold = Math.Max(0, product.Sold - item.Quantity);
            }
        }

        await db.SaveChangesAsync(ct);
        foreach (var customerId in changed.Select(o => o.CustomerId).Distinct())
        {
            await CustomerAggregates.RecomputeAsync(db, customerId, ct);
        }

        await db.SaveChangesAsync(ct);
    }
}
