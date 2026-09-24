using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nebula.Application.Common;
using Nebula.Domain;
using Nebula.Infrastructure.Persistence;
using Nebula.IntegrationTests.Infrastructure;

namespace Nebula.IntegrationTests;

public sealed class SeedDataTests(NebulaApiFactory factory) : IClassFixture<NebulaApiFactory>
{
    [Fact]
    public async Task Database_is_seeded_on_startup()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Equal(4800, await db.Orders.CountAsync(ct));
        Assert.Equal(700, await db.Customers.CountAsync(ct));
        Assert.Equal(60, await db.Products.CountAsync(ct));
        Assert.Equal("usr_1", (await db.Users.SingleAsync(ct)).Id);

        var first = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == "ord_000001", ct);
        Assert.Equal(1001, first.Number);
        Assert.NotEmpty(first.Items);
        Assert.Equal(OrderStatus.New, first.History[0].Status);
        Assert.True(await db.Orders.MaxAsync(o => o.CreatedAt, ct) <= NebulaApiFactory.Now);
    }

    [Fact]
    public async Task Reset_restores_seed()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var product = await db.Products.FirstAsync(p => p.Id == "prd_0001", ct);
            db.Products.Remove(product);
            await db.SaveChangesAsync(ct);
            Assert.Equal(59, await db.Products.CountAsync(ct));
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IDemoResetter>().ResetAsync(ct);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(60, await db.Products.CountAsync(ct));
            Assert.Equal(4800, await db.Orders.CountAsync(ct));
            Assert.Equal(700, await db.Customers.CountAsync(ct));
            Assert.Equal(1, await db.Users.CountAsync(ct));
            Assert.True(await db.Products.AnyAsync(p => p.Id == "prd_0001", ct));
        }
    }
}
