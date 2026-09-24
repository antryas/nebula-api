namespace Nebula.Api.OpenApi;

public static class OpenApiSetup
{
    public const string DocumentName = "v1";

    public static IServiceCollection AddNebulaOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi(DocumentName, options =>
        {
            options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
            options.AddOperationTransformer<BearerSecuritySchemeTransformer>();
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = "Nebula Commerce API";
                document.Info.Version = DocumentName;
                document.Info.Description =
                    "Backend for the Nebula Commerce admin dashboard. Sign in with POST /api/auth/login "
                    + "(any email, password of 6+ characters, e.g. alex@nebula.store / demo1234), "
                    + "then press Authorize and paste the token. The demo database is re-seeded every 6 hours.";
                return Task.CompletedTask;
            });
        });
        return services;
    }

    /// <summary>Serves /openapi/v1.json and Swagger UI at /swagger in every environment (it is a showcase).</summary>
    public static WebApplication UseNebulaOpenApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapOpenApi();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint($"/openapi/{DocumentName}.json", "Nebula API v1");
            options.RoutePrefix = "swagger";
            options.DocumentTitle = "Nebula API";
        });
        app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
        return app;
    }
}
