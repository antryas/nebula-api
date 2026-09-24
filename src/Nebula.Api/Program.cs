using System.Diagnostics;
using Nebula.Api.Health;
using Nebula.Application;
using Nebula.Infrastructure;
using Nebula.Infrastructure.Persistence;
using Nebula.Infrastructure.Seeding;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database");

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var stopwatch = Stopwatch.StartNew();
    await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().CreateAsync();
    app.Logger.LogInformation("Database created and seeded in {ElapsedMs} ms", stopwatch.ElapsedMilliseconds);
}

app.MapHealthChecks("/health", new() { ResponseWriter = HealthResponse.WriteAsync });

await app.RunAsync();

public partial class Program;
