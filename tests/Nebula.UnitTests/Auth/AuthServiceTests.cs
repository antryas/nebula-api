using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Nebula.Application.Auth;
using Nebula.Application.Common;
using Nebula.Domain;
using Nebula.Infrastructure.Persistence;

namespace Nebula.UnitTests.Auth;

public sealed class AuthServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private AppDbContext _db = null!;

    public async ValueTask InitializeAsync()
    {
        await _connection.OpenAsync(TestContext.Current.CancellationToken);
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        await _db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        _db.Users.Add(new User
        {
            Id = "usr_1",
            Name = "Alex Morgan",
            Email = "alex@nebula.store",
            AvatarUrl = "https://example.test/alex.png",
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Valid_credentials_return_token_and_demo_user()
    {
        var service = new AuthService(_db, new FakeTokenIssuer());

        var result = await service.LoginAsync(
            new LoginRequest("someone@example.com", "secret1"), TestContext.Current.CancellationToken);

        Assert.Equal("token-for-usr_1", result.Token);
        Assert.Equal(new UserDto("usr_1", "Alex Morgan", "alex@nebula.store", "https://example.test/alex.png", "Admin"), result.User);
    }

    public static TheoryData<string?, string?> InvalidCredentials => new()
    {
        { "alex@nebula.store", "12345" },
        { "alex@nebula.store", null },
        { "", "demo1234" },
        { "   ", "demo1234" },
        { null, "demo1234" },
    };

    [Theory]
    [MemberData(nameof(InvalidCredentials))]
    public async Task Invalid_credentials_throw_401(string? email, string? password)
    {
        var service = new AuthService(_db, new FakeTokenIssuer());

        var error = await Assert.ThrowsAsync<ApiException>(() =>
            service.LoginAsync(new LoginRequest(email, password), TestContext.Current.CancellationToken));

        Assert.Equal(401, error.Status);
        Assert.Equal("invalid_credentials", error.Code);
        Assert.Equal("Invalid email or password", error.Message);
    }

    [Fact]
    public async Task Missing_body_throws_401()
    {
        var service = new AuthService(_db, new FakeTokenIssuer());

        var error = await Assert.ThrowsAsync<ApiException>(() =>
            service.LoginAsync(null, TestContext.Current.CancellationToken));

        Assert.Equal("invalid_credentials", error.Code);
    }

    private sealed class FakeTokenIssuer : ITokenIssuer
    {
        public string Issue(User user) => $"token-for-{user.Id}";
    }
}
