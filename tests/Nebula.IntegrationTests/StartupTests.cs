using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;
using Nebula.IntegrationTests.Infrastructure;

namespace Nebula.IntegrationTests;

public sealed class StartupTests
{
    [Fact]
    public async Task Production_start_without_signing_key_fails_fast()
    {
        await using var factory = new ProductionWithoutKeyFactory();

        var error = Assert.ThrowsAny<OptionsValidationException>(() => factory.CreateClient());

        Assert.Contains("Jwt:SigningKey", error.Message, StringComparison.Ordinal);
    }

    private sealed class ProductionWithoutKeyFactory : NebulaApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseEnvironment("Production");
            builder.UseSetting("Jwt:SigningKey", "");
        }
    }
}
