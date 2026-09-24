using Nebula.Domain;

namespace Nebula.Application.Auth;

public sealed record LoginRequest(string? Email, string? Password);

public sealed record LoginResponse(string Token, UserDto User);

public sealed record UserDto(string Id, string Name, string Email, string AvatarUrl, string Role)
{
    public static UserDto From(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return new UserDto(user.Id, user.Name, user.Email, user.AvatarUrl, user.Role);
    }
}
