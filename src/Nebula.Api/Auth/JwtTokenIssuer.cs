using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Nebula.Application.Auth;
using Nebula.Domain;

namespace Nebula.Api.Auth;

/// <summary>Issues HS256 access tokens. Uses wall-clock time, not the seed clock, so tokens validate.</summary>
public sealed class JwtTokenIssuer(IOptions<JwtOptions> options, TimeProvider time) : ITokenIssuer
{
    private readonly JsonWebTokenHandler _handler = new();

    public string Issue(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var jwt = options.Value;
        var now = time.GetUtcNow().UtcDateTime;

        return _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.Add(jwt.Lifetime),
            SigningCredentials = new SigningCredentials(jwt.CreateSigningKey(), SecurityAlgorithms.HmacSha256),
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id),
                new Claim(JwtRegisteredClaimNames.Name, user.Name),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim("role", user.Role),
            ]),
        });
    }
}
