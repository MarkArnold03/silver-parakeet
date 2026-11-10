namespace Parakeet.Application.Auth;

/// <summary>Section "Auth" in appsettings. The signing key must come from the environment in production.</summary>
public class AuthOptions
{
    public const string Issuer = "parakeet";
    public const string DispatchAudience = "dispatch";

    /// <summary>Secret used to sign dispatcher access tokens. At least 32 characters.</summary>
    public string JwtSigningKey { get; set; } = "";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 14;
    public int LoginsPerMinutePerIp { get; set; } = 10;
}
