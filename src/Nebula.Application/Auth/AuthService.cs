using Microsoft.EntityFrameworkCore;
using Nebula.Application.Common;

namespace Nebula.Application.Auth;

/// <summary>
/// Demo sign-in, mirroring the Angular mock: any non-blank email with a password of at least
/// six characters signs in as the single seeded admin user.
/// </summary>
public sealed class AuthService(IAppDbContext db, ITokenIssuer tokens)
{
    public const int MinPasswordLength = 6;

    public async Task<LoginResponse> LoginAsync(LoginRequest? request, CancellationToken ct)
    {
        if (request is null
            || string.IsNullOrWhiteSpace(request.Email)
            || request.Password is null
            || request.Password.Length < MinPasswordLength)
        {
            throw new ApiException(401, "invalid_credentials", "Invalid email or password");
        }

        var user = await db.Users.AsNoTracking().OrderBy(u => u.Id).FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("The demo user is missing from the database.");

        return new LoginResponse(tokens.Issue(user), UserDto.From(user));
    }
}
