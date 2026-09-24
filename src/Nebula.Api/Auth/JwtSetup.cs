using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Nebula.Api.Errors;
using Nebula.Application.Auth;

namespace Nebula.Api.Auth;

public sealed class JwtOptions
{
    public const string Section = "Jwt";
    public const int MinKeyBytes = 32;

    public string Issuer { get; set; } = "nebula-api";
    public string Audience { get; set; } = "nebula-api";
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromHours(8);
    public string SigningKey { get; set; } = "";

    public SymmetricSecurityKey CreateSigningKey() => new(Encoding.UTF8.GetBytes(SigningKey));
}

public static class JwtSetup
{
    public static IServiceCollection AddNebulaJwt(this IServiceCollection services)
    {
        services.AddOptions<JwtOptions>()
            .BindConfiguration(JwtOptions.Section)
            .Validate(
                o => Encoding.UTF8.GetByteCount(o.SigningKey) >= JwtOptions.MinKeyBytes,
                $"Jwt:SigningKey must be set to at least {JwtOptions.MinKeyBytes} bytes (env Jwt__SigningKey).")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ITokenIssuer, JwtTokenIssuer>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwtOptions) =>
            {
                var jwt = jwtOptions.Value;
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = jwt.CreateSigningKey(),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ClockSkew = TimeSpan.FromMinutes(1),
                    NameClaimType = "name",
                    RoleClaimType = "role",
                };
                bearer.Events = new JwtBearerEvents
                {
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        context.Response.Headers.WWWAuthenticate = "Bearer";
                        await ProblemDetailsSetup.WriteAsync(
                            context.HttpContext, StatusCodes.Status401Unauthorized, "unauthorized", "Sign in to use the API");
                    },
                };
            });

        services.AddAuthorization();
        return services;
    }
}
