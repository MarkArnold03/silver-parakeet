using Parakeet.Domain.Deliveries;

namespace Parakeet.Application.Customers;

/// <summary>Section "CustomerLinks" in appsettings. The signing key must come from the environment in production.</summary>
public class CustomerLinkOptions
{
    public const string Audience = "customer";
    public const string DeliveryIdClaim = "delivery_id";
    public const string LinkVersionClaim = "lv";

    /// <summary>Secret used to sign customer links. Not the same as Auth:JwtSigningKey. At least 32 characters.</summary>
    public string SigningKey { get; set; } = "";
    /// <summary>Where the customer app is served. The link is {BaseUrl}/b/{token}.</summary>
    public string BaseUrl { get; set; } = "http://localhost:5173";
    /// <summary>A backstop only. The real expiry is checked against the delivery date on every request.</summary>
    public int HardExpiryDays { get; set; } = 120;
}

public interface ICustomerLinks
{
    /// <summary>A link for the delivery's current link version. Older versions stop working when it is bumped.</summary>
    string CreateLink(Delivery delivery);
}

/// <summary>What a valid customer token proves: which delivery, and which version of its link.</summary>
public record CustomerAccess(Guid DeliveryId, int LinkVersion);
