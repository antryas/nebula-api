using Microsoft.Extensions.DependencyInjection;
using Nebula.Application.Analytics;
using Nebula.Application.Auth;

namespace Nebula.Application;

public static class DependencyInjection
{
    /// <summary>Registers application services and validators.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<AuthService>();
        services.AddScoped<AnalyticsService>();
        return services;
    }
}
