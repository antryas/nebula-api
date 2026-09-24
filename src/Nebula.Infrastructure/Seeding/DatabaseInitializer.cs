using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Nebula.Application.Common;
using Nebula.Infrastructure.Persistence;

namespace Nebula.Infrastructure.Seeding;

/// <summary>Creates and re-seeds the SQLite demo database.</summary>
public sealed class DatabaseInitializer(AppDbContext db, IClock clock)
{
    // Child tables first so the deletes never depend on cascade behaviour.
    private const string DeleteAllSql = """
        DELETE FROM "OrderStatusHistory";
        DELETE FROM "OrderItems";
        DELETE FROM "Orders";
        DELETE FROM "ProductVariants";
        DELETE FROM "Products";
        DELETE FROM "Customers";
        DELETE FROM "Users";
        """;

    /// <summary>Seed anchor: the current time truncated to the hour.</summary>
    public static DateTime AnchorFor(DateTime utcNow) =>
        new(utcNow.Ticks - (utcNow.Ticks % TimeSpan.TicksPerHour), DateTimeKind.Utc);

    /// <summary>Drops the database file, recreates the schema and inserts the seed.</summary>
    public async Task CreateAsync(CancellationToken ct = default)
    {
        await db.Database.EnsureDeletedAsync(ct);
        DeleteWalFiles();
        await db.Database.EnsureCreatedAsync(ct);
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct);
        await InsertSeedAsync(ct);
    }

    /// <summary>Replaces all data with a fresh seed in a single transaction.</summary>
    public async Task ResetAsync(CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync(DeleteAllSql, ct);
        await InsertSeedAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private async Task InsertSeedAsync(CancellationToken ct)
    {
        var seed = DeterministicSeeder.Create(AnchorFor(clock.UtcNow));

        var tracker = db.ChangeTracker;
        var autoDetect = tracker.AutoDetectChangesEnabled;
        tracker.AutoDetectChangesEnabled = false;
        try
        {
            db.Products.AddRange(seed.Products);
            db.Customers.AddRange(seed.Customers);
            db.Orders.AddRange(seed.Orders);
            db.Users.Add(seed.User);
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            tracker.Clear();
            tracker.AutoDetectChangesEnabled = autoDetect;
        }
    }

    /// <summary>Removes stale WAL side files so they can never be replayed into a new database.</summary>
    private void DeleteWalFiles()
    {
        var dataSource = new SqliteConnectionStringBuilder(db.Database.GetConnectionString()).DataSource;
        if (string.IsNullOrEmpty(dataSource) || dataSource == ":memory:")
        {
            return;
        }

        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            File.Delete(dataSource + suffix);
        }
    }
}
