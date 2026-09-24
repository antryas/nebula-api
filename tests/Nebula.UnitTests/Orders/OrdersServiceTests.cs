using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Nebula.Application.Common;
using Nebula.Application.Customers;
using Nebula.Application.Orders;
using Nebula.Domain;
using Nebula.Infrastructure.Persistence;
using Nebula.UnitTests.TestSupport;

namespace Nebula.UnitTests.Orders;

public sealed class OrdersServiceTests : IAsyncLifetime
{
    private SqliteTestDb _db = null!;

    public async ValueTask InitializeAsync() => _db = await SqliteTestDb.CreateAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    private static OrderListQuery Query(
        string? status = null, string? from = null, string? to = null,
        string? search = null, string? sort = null, string? dir = null, string? page = null, string? pageSize = null) =>
        OrderListQuery.Parse(ListQuery.Parse(page, pageSize, sort, dir, search), status, from, to);

    private static OrdersService Service(AppDbContext context, DateTime? now = null) =>
        new(context, new FakeClock(now ?? SqliteTestDb.Now));

    private async Task<Paged<OrderDto>> ListAsync(OrderListQuery q)
    {
        await using var context = _db.CreateContext();
        return await Service(context).ListAsync(q, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Default_list_is_newest_first()
    {
        var result = await ListAsync(Query());

        Assert.Equal(5, result.Total);
        Assert.Equal([1005, 1004, 1003, 1002, 1001], result.Items.Select(o => o.Number));
        Assert.Equal(1, result.Page);
        Assert.Equal(20, result.PageSize);
    }

    [Fact]
    public async Task Filters_by_status_list()
    {
        var result = await ListAsync(Query(status: "new, packing,bogus"));

        Assert.Equal([1005, 1003], result.Items.Select(o => o.Number));
    }

    [Fact]
    public async Task Status_filter_with_only_unknown_values_matches_nothing()
    {
        var result = await ListAsync(Query(status: "bogus"));

        Assert.Equal(0, result.Total);
    }

    [Fact]
    public async Task Date_range_is_inclusive()
    {
        var from = SqliteTestDb.Now.AddDays(-5).ToString("O", CultureInfo.InvariantCulture);
        var to = SqliteTestDb.Now.AddDays(-2).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

        var result = await ListAsync(Query(from: from, to: to));

        Assert.Equal([1004, 1003, 1002], result.Items.Select(o => o.Number));
    }

    [Fact]
    public async Task Unparseable_dates_are_ignored()
    {
        var result = await ListAsync(Query(from: "yesterday", to: "soon"));

        Assert.Equal(5, result.Total);
    }

    [Theory]
    [InlineData("1003", new[] { 1003 })]
    [InlineData("BOB@SHOP", new[] { 1005, 1002 })]
    [InlineData(" ann lee ", new[] { 1003, 1001 })]
    [InlineData("%", new int[0])]
    public async Task Searches_number_name_and_email(string search, int[] expected)
    {
        var result = await ListAsync(Query(search: search));

        Assert.Equal(expected, result.Items.Select(o => o.Number));
    }

    [Fact]
    public async Task Sorts_by_total_ascending()
    {
        var result = await ListAsync(Query(sort: "total", dir: "asc"));

        Assert.Equal([1003, 1001, 1004, 1005, 1002], result.Items.Select(o => o.Number));
    }

    [Fact]
    public async Task Sorts_by_customer_name_case_insensitively()
    {
        var result = await ListAsync(Query(sort: "customerName", dir: "asc"));

        Assert.Equal(["Ann Lee", "Ann Lee", "Bob Stone", "Bob Stone", "Cara Diaz"], result.Items.Select(o => o.CustomerName));
    }

    [Fact]
    public async Task Unknown_sort_key_falls_back_to_created_at()
    {
        var result = await ListAsync(Query(sort: "items"));

        Assert.Equal([1005, 1004, 1003, 1002, 1001], result.Items.Select(o => o.Number));
    }

    [Fact]
    public async Task Pages_with_totals()
    {
        var result = await ListAsync(Query(page: "2", pageSize: "2"));

        Assert.Equal(5, result.Total);
        Assert.Equal(2, result.Page);
        Assert.Equal(2, result.PageSize);
        Assert.Equal([1003, 1002], result.Items.Select(o => o.Number));
    }

    [Fact]
    public async Task Page_past_the_end_is_empty()
    {
        var result = await ListAsync(Query(page: "99999999999"));

        Assert.Empty(result.Items);
        Assert.Equal(5, result.Total);
        Assert.Equal(int.MaxValue, result.Page);
    }

    [Fact]
    public async Task Dto_carries_items_address_history_and_rounded_money()
    {
        var order = (await ListAsync(Query(search: "1001"))).Items.Single();

        Assert.Equal("ord_000001", order.Id);
        Assert.Equal(OrderStatus.Delivered, order.Status);
        Assert.Equal(40m, order.Subtotal);
        Assert.Equal(7.99m, order.Shipping);
        Assert.Equal(3.2m, order.Tax);
        Assert.Equal(51.19m, order.Total);
        Assert.Single(order.Items);
        Assert.Equal("1001 Main St", order.ShippingAddress.Line1);
        Assert.Equal("Order placed", order.History.Single().Note);
    }

    [Fact]
    public async Task Get_unknown_order_throws_not_found()
    {
        await using var context = _db.CreateContext();

        var ex = await Assert.ThrowsAsync<NotFoundException>(
            () => Service(context).GetAsync("ord_999999", TestContext.Current.CancellationToken));

        Assert.Equal("Order not found", ex.Message);
    }

    [Fact]
    public async Task Update_status_appends_history()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = SqliteTestDb.Now.AddMinutes(5);
        await using (var context = _db.CreateContext())
        {
            var dto = await Service(context, now).UpdateStatusAsync("ord_000003", new UpdateStatusRequest("packing"), ct);

            Assert.Equal(OrderStatus.Packing, dto.Status);
            Assert.Equal(2, dto.History.Count);
            Assert.Equal(OrderStatus.Packing, dto.History[1].Status);
            Assert.Equal(now, dto.History[1].At);
            Assert.Null(dto.History[1].Note);
        }

        await using (var context = _db.CreateContext())
        {
            var stored = await context.Orders.AsNoTracking().SingleAsync(o => o.Id == "ord_000003", ct);
            Assert.Equal(OrderStatus.Packing, stored.Status);
            Assert.Equal([OrderStatus.New, OrderStatus.Packing], stored.History.Select(h => h.Status));
        }
    }

    [Fact]
    public async Task Same_status_is_a_no_op()
    {
        await using var context = _db.CreateContext();

        var dto = await Service(context).UpdateStatusAsync("ord_000003", new UpdateStatusRequest("new"), TestContext.Current.CancellationToken);

        Assert.Single(dto.History);
    }

    [Fact]
    public async Task Cancelling_recomputes_customer_aggregates_and_product_sold()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var context = _db.CreateContext())
        {
            await CustomerAggregates.RecomputeAsync(context, "cus_0002", ct);
            await context.SaveChangesAsync(ct);
            var before = await context.Customers.AsNoTracking().SingleAsync(c => c.Id == "cus_0002", ct);
            Assert.Equal(2, before.OrdersCount);
            Assert.Equal(SqliteTestDb.Now.AddDays(-1), before.LastOrderAt);
        }

        await using (var context = _db.CreateContext())
        {
            await Service(context).UpdateStatusAsync("ord_000005", new UpdateStatusRequest("cancelled"), ct);
        }

        await using (var context = _db.CreateContext())
        {
            var customer = await context.Customers.AsNoTracking().SingleAsync(c => c.Id == "cus_0002", ct);
            Assert.Equal(1, customer.OrdersCount);
            Assert.Equal(172.8m, customer.LifetimeValue);
            Assert.Equal(SqliteTestDb.Now.AddDays(-5), customer.LastOrderAt);
            Assert.Equal(4, (await context.Products.AsNoTracking().SingleAsync(p => p.Id == "prd_0001", ct)).Sold);
        }
    }

    [Theory]
    [InlineData("ord_000001", 1001, "delivered")]
    [InlineData("ord_000004", 1004, "cancelled")]
    public async Task Closed_order_cannot_change_status(string id, int number, string current)
    {
        await using var context = _db.CreateContext();

        var ex = await Assert.ThrowsAsync<ApiException>(
            () => Service(context).UpdateStatusAsync(id, new UpdateStatusRequest("shipped"), TestContext.Current.CancellationToken));

        Assert.Equal(422, ex.Status);
        Assert.Equal("invalid_transition", ex.Code);
        Assert.Equal($"Order #{number} is {current} and can no longer change status", ex.Message);
    }

    [Theory]
    [InlineData("Shipped")]
    [InlineData("lost")]
    [InlineData(null)]
    public async Task Unknown_status_is_a_validation_error(string? status)
    {
        await using var context = _db.CreateContext();

        var ex = await Assert.ThrowsAsync<ValidationFailedException>(
            () => Service(context).UpdateStatusAsync("ord_000003", new UpdateStatusRequest(status), TestContext.Current.CancellationToken));

        Assert.Equal("Unknown order status", ex.Message);
        Assert.Equal("Unknown order status", ex.Details!["status"]);
    }

    [Fact]
    public async Task Unknown_order_wins_over_missing_body()
    {
        await using var context = _db.CreateContext();

        await Assert.ThrowsAsync<NotFoundException>(
            () => Service(context).UpdateStatusAsync("ord_999999", null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Bulk_update_skips_closed_and_unknown_orders()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var context = _db.CreateContext())
        {
            var result = await Service(context).BulkUpdateStatusAsync(
                new BulkStatusRequest(["ord_000001", "ord_000002", "ord_000003", "ord_000004", "nope"], "shipped"), ct);

            // ord_000002 is already shipped: the mock still counts it but records no history.
            Assert.Equal(2, result.Updated);
        }

        await using (var context = _db.CreateContext())
        {
            var orders = await context.Orders.AsNoTracking().OrderBy(o => o.Number).ToListAsync(ct);
            Assert.Equal(
                [OrderStatus.Delivered, OrderStatus.Shipped, OrderStatus.Shipped, OrderStatus.Cancelled, OrderStatus.Packing],
                orders.Select(o => o.Status));
            Assert.Single(orders[1].History);
            Assert.Equal(2, orders[2].History.Count);
        }
    }

    [Theory]
    [InlineData(false, "shipped")]
    [InlineData(true, "lost")]
    [InlineData(true, null)]
    public async Task Bulk_update_validates_body(bool withIds, string? status)
    {
        await using var context = _db.CreateContext();

        var ex = await Assert.ThrowsAsync<ValidationFailedException>(() => Service(context).BulkUpdateStatusAsync(
            new BulkStatusRequest(withIds ? ["ord_000003"] : null, status), TestContext.Current.CancellationToken));

        Assert.Equal("Expected { ids: string[]; status }", ex.Message);
        Assert.Null(ex.Details);
    }
}
