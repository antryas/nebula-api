using System.Diagnostics;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Nebula.Api.Auth;
using Nebula.Api.Endpoints;
using Nebula.Api.Errors;
using Nebula.Api.Health;
using Nebula.Api.OpenApi;
using Nebula.Api.Security;
using Nebula.Application;
using Nebula.Infrastructure;
using Nebula.Infrastructure.Persistence;
using Nebula.Infrastructure.Seeding;

var builder = WebApplication.CreateBuilder(args);

builder.AddNebulaHosting();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddNebulaProblemDetails();
builder.Services.AddNebulaJwt();
builder.Services.AddNebulaCors();
builder.Services.AddNebulaRateLimiting();
builder.Services.AddNebulaOpenApi();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database");

var app = builder.Build();

// Fail fast on a missing or weak signing key before spending time on the seed.
_ = app.Services.GetRequiredService<IOptions<JwtOptions>>().Value;

await using (var scope = app.Services.CreateAsyncScope())
{
    var stopwatch = Stopwatch.StartNew();
    await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().CreateAsync();
    app.Logger.LogInformation("Database created and seeded in {ElapsedMs} ms", stopwatch.ElapsedMilliseconds);
}

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseNebulaStatusCodePages();
app.UseResponseCompression();
app.UseRouting();
app.UseCors(CorsSetup.FrontendPolicy);
app.UseRequestBodyLimit();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health", new() { ResponseWriter = HealthResponse.WriteAsync });
app.UseNebulaOpenApi();
app.MapNebulaEndpoints();

await app.RunAsync();

public partial class Program;
