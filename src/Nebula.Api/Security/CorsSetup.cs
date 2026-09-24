using Microsoft.AspNetCore.Cors.Infrastructure;

namespace Nebula.Api.Security;

public static class CorsSetup
{
    public const string FrontendPolicy = "frontend";

    public static IServiceCollection AddNebulaCors(this IServiceCollection services)
    {
        services.AddCors();
        // Configured lazily so hosts (and tests) can override Cors:AllowedOrigins after registration.
        services.AddOptions<CorsOptions>().Configure<IConfiguration>((options, configuration) =>
        {
            var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
            options.AddPolicy(FrontendPolicy, policy => policy
                .WithOrigins(origins)
                .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")
                .WithHeaders("Authorization", "Content-Type")
                .SetPreflightMaxAge(TimeSpan.FromMinutes(10)));
        });
        return services;
    }
}
