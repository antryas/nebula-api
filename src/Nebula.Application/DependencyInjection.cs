using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Nebula.Application.Ai;
using Nebula.Application.Analytics;
using Nebula.Application.Auth;
using Nebula.Application.Common;
using Nebula.Application.Customers;
using Nebula.Application.Orders;
using Nebula.Application.Products;

namespace Nebula.Application;

public static class DependencyInjection
{
    /// <summary>Registers application services and validators.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // One write lock per app (and database); see WriteGate.
        services.AddSingleton<WriteGate>();
        services.AddScoped<AuthService>();
        services.AddScoped<AnalyticsService>();
        services.AddScoped<OrdersService>();
        services.AddScoped<CustomersService>();
        services.AddScoped<ProductsService>();
        services.AddSingleton<IValidator<ProductInput>, ProductInputValidator>();
        services.AddScoped(sp => new LiveOrderFactory(
            sp.GetRequiredService<IAppDbContext>(), sp.GetRequiredService<IClock>(), Random.Shared, sp.GetRequiredService<WriteGate>()));

        // AI: the quota must outlive requests; the provider's IChatClient is registered by Infrastructure.
        services.AddSingleton<AiQuotaTracker>();
        services.AddScoped<StoreDataTools>();
        services.AddScoped<AiAssistant>();
        services.AddSingleton<IValidator<AskRequest>, AskRequestValidator>();
        services.AddSingleton<IValidator<ProductDescriptionRequest>, ProductDescriptionRequestValidator>();
        return services;
    }
}
