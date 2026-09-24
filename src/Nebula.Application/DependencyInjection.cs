using Microsoft.Extensions.DependencyInjection;
using Nebula.Application.Auth;

namespace Nebula.Application;

public static class DependencyInjection
{
    /// <summary>Registers application services and validators.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<AuthService>();
        return services;
    }
}
