using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Parakeet.Application.Auth;
using Parakeet.Application.Customers;
using Parakeet.Domain.Deliveries;

namespace Parakeet.Infrastructure.Auth;

public class JwtCustomerLinks(IOptions<CustomerLinkOptions> options, TimeProvider time) : ICustomerLinks
{
    public string CreateLink(Delivery delivery)
    {
        var now = time.GetUtcNow();
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = AuthOptions.Issuer,
            Audience = CustomerLinkOptions.Audience,
            Subject = new ClaimsIdentity(
            [
                new Claim(CustomerLinkOptions.DeliveryIdClaim, delivery.Id.ToString()),
                new Claim(CustomerLinkOptions.LinkVersionClaim, delivery.LinkVersion.ToString(), ClaimValueTypes.Integer32),
            ]),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = now.AddDays(options.Value.HardExpiryDays).UtcDateTime,
            SigningCredentials = new SigningCredentials(
                JwtAccessTokenIssuer.KeyFrom(options.Value.SigningKey, "CustomerLinks:SigningKey"), SecurityAlgorithms.HmacSha256),
        });
        return $"{options.Value.BaseUrl.TrimEnd('/')}/b/{token}";
    }
}
