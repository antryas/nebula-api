using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nebula.Application.Common;
using Nebula.Infrastructure.Ai;
using Nebula.Infrastructure.Persistence;
using Nebula.Infrastructure.Seeding;
using Nebula.Infrastructure.Time;

namespace Nebula.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            // Resolved lazily so test hosts can override Database:Path after registration.
            var path = sp.GetRequiredService<IConfiguration>()["Database:Path"];
            if (string.IsNullOrWhiteSpace(path))
            {
                path = Path.Combine(Path.GetTempPath(), "nebula-api", "nebula.db");
            }

            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Orders own two collections (items, history): split queries avoid a cartesian join.
            options.UseSqlite($"Data Source={path}", sqlite => sqlite.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));
        });
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IDryRunner, DryRunner>();
        services.TryAddSingleton<IClock, SystemClock>();

        services.AddScoped<DatabaseInitializer>();
        services.AddScoped<IDemoResetter, DemoResetter>();
        services.AddHostedService<PeriodicResetService>();

        services.AddAiClient();

        return services;
    }
}
