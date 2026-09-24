using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Nebula.Api.Errors;

namespace Nebula.Api.Security;

public static class RateLimitSetup
{
    public const string ApiPolicy = "api";

    public static IServiceCollection AddNebulaRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, _) =>
            {
                var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait)
                    ? wait
                    : TimeSpan.FromSeconds(ReadWindowSeconds(context.HttpContext.RequestServices.GetRequiredService<IConfiguration>()));
                context.HttpContext.Response.Headers.RetryAfter =
                    Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                await ProblemDetailsSetup.WriteAsync(
                    context.HttpContext,
                    StatusCodes.Status429TooManyRequests,
                    "rate_limited",
                    "Too many requests, slow down a little");
            };
        });

        // Configured lazily so hosts (and tests) can override RateLimiting:* after registration.
        services.AddOptions<RateLimiterOptions>().Configure<IConfiguration>((options, configuration) =>
        {
            var permitLimit = configuration.GetValue("RateLimiting:PermitLimit", 120);
            var window = TimeSpan.FromSeconds(ReadWindowSeconds(configuration));
            options.AddPolicy(ApiPolicy, httpContext => RateLimitPartition.GetFixedWindowLimiter(
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit,
                    Window = window,
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));
        });

        return services;
    }

    private static int ReadWindowSeconds(IConfiguration configuration) =>
        configuration.GetValue("RateLimiting:WindowSeconds", 60);
}
