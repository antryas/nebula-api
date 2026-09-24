using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
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
        services.AddScoped<AuthService>();
        services.AddScoped<AnalyticsService>();
        services.AddScoped<OrdersService>();
        services.AddScoped<CustomersService>();
        services.AddScoped<ProductsService>();
        services.AddSingleton<IValidator<ProductInput>, ProductInputValidator>();
        services.AddScoped(sp => new LiveOrderFactory(
            sp.GetRequiredService<IAppDbContext>(), sp.GetRequiredService<IClock>(), Random.Shared));
        return services;
    }
}
