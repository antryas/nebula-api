using Nebula.Domain;

namespace Nebula.Application.Auth;

/// <summary>Issues a signed access token for a signed-in user.</summary>
public interface ITokenIssuer
{
    string Issue(User user);
}
