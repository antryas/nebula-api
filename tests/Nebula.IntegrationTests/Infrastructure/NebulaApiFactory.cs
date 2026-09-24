using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Nebula.Application.Common;

namespace Nebula.IntegrationTests.Infrastructure;

public class NebulaApiFactory : WebApplicationFactory<Program>
{
    public static readonly DateTime Now = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), "nebula-tests", $"{Guid.NewGuid():N}.db");

    protected virtual IDictionary<string, string?> Settings => new Dictionary<string, string?>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:Path", _dbPath);
        // No background re-seeding while tests run.
        builder.UseSetting("Demo:ResetInterval", "00:00:00");
        foreach (var (key, value) in Settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureServices(services => services.AddSingleton<IClock>(new FixedClock(Now)));
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        DeleteDatabaseFiles();
        GC.SuppressFinalize(this);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            DeleteDatabaseFiles();
        }
    }

    private void DeleteDatabaseFiles()
    {
        // Pooled SQLite connections keep the file locked on Windows.
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
