using Nebula.Api.Health;
using Nebula.Application;
using Nebula.Infrastructure;
using Nebula.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database");

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    // Replaced by the seeding DatabaseInitializer in Task 2.
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();
}

app.MapHealthChecks("/health", new() { ResponseWriter = HealthResponse.WriteAsync });

await app.RunAsync();

public partial class Program;
