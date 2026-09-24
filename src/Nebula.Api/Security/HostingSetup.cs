using System.Net;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Nebula.Api.Errors;

namespace Nebula.Api.Security;

/// <summary>Reverse-proxy awareness, request size limits, compression and Kestrel hardening.</summary>
public static class HostingSetup
{
    public const long MaxRequestBodyBytes = 64 * 1024;

    public static WebApplicationBuilder AddNebulaHosting(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;
            kestrel.Limits.MaxRequestBodySize = MaxRequestBodyBytes;
        });

        builder.Services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
        });

        builder.Services.AddOptions<ForwardedHeadersOptions>().Configure<IConfiguration>((options, configuration) =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            // Loopback is trusted by default; the compose file adds the Docker gateway.
            foreach (var proxy in configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
            {
                if (IPAddress.TryParse(proxy, out var address))
                {
                    options.KnownProxies.Add(address);
                }
            }
        });

        return builder;
    }

    /// <summary>
    /// Enforces the body limit in every host: Kestrel honours <see cref="IHttpMaxRequestBodySizeFeature"/>,
    /// but other servers (e.g. the test server) do not, so declared oversized bodies are rejected up front.
    /// </summary>
    public static IApplicationBuilder UseRequestBodyLimit(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (context.Request.ContentLength > MaxRequestBodyBytes)
            {
                await ProblemDetailsSetup.WriteAsync(
                    context,
                    StatusCodes.Status413PayloadTooLarge,
                    ErrorCodes.ForStatus(StatusCodes.Status413PayloadTooLarge),
                    "Request body is too large");
                return;
            }

            var sizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (sizeFeature is { IsReadOnly: false })
            {
                sizeFeature.MaxRequestBodySize = MaxRequestBodyBytes;
            }

            await next(context);
        });
}
