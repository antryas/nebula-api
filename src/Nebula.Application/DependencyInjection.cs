using Microsoft.Extensions.DependencyInjection;

namespace Nebula.Application;

public static class DependencyInjection
{
    /// <summary>Registers application services and validators.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        return services;
    }
}
