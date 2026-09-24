namespace Nebula.Api.OpenApi;

public static class OpenApiSetup
{
    public const string DocumentName = "v1";

    public static IServiceCollection AddNebulaOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi(DocumentName, options =>
        {
            options.CreateSchemaReferenceId = ContractSchemaTransformer.CreateSchemaReferenceId;
            options.AddDocumentTransformer<NebulaDocumentTransformer>();
            options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
            options.AddOperationTransformer<BearerSecuritySchemeTransformer>();
            options.AddSchemaTransformer<ContractSchemaTransformer>();
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
