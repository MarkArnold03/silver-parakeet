using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Parakeet.Application.Auth;
using Parakeet.Domain.Users;

namespace Parakeet.Infrastructure.Auth;

public class JwtAccessTokenIssuer(IOptions<AuthOptions> options, TimeProvider time) : IAccessTokenIssuer
{
    public static SymmetricSecurityKey KeyFrom(string signingKey)
    {
        if (signingKey.Length < 32)
            throw new InvalidOperationException("Auth:JwtSigningKey must be set to at least 32 characters.");
        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
    }

    public AccessToken Issue(User user)
    {
        var now = time.GetUtcNow();
        var expires = now.AddMinutes(options.Value.AccessTokenMinutes);
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = AuthOptions.Issuer,
            Audience = AuthOptions.DispatchAudience,
            Subject = new ClaimsIdentity(
            [
                new Claim("sub", user.Id.ToString()),
                new Claim("name", user.Name),
                new Claim("role", user.Role.ToString()),
            ]),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(KeyFrom(options.Value.JwtSigningKey), SecurityAlgorithms.HmacSha256),
        });
        return new AccessToken(token, expires);
    }
}
