using System.ComponentModel.DataAnnotations;
using Parakeet.Application.Auth;

namespace Parakeet.Api.Endpoints;

public record LoginRequest([Required] string Email, [Required] string Password);
public record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, UserDto User);

public static class AuthEndpoints
{
    public const string RefreshCookie = "parakeet_refresh";
    public const string LoginRateLimit = "login";
    private const string CookiePath = "/api/auth";

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/login", async (LoginRequest request, AuthService auth, HttpContext http, CancellationToken ct) =>
            Respond(http, await auth.LoginAsync(request.Email, request.Password, ct)))
            .RequireRateLimiting(LoginRateLimit);

        group.MapPost("/refresh", async (AuthService auth, HttpContext http, CancellationToken ct) =>
            Respond(http, await auth.RefreshAsync(http.Request.Cookies[RefreshCookie], ct)));

        group.MapPost("/logout", async (AuthService auth, HttpContext http, CancellationToken ct) =>
        {
            await auth.LogoutAsync(http.Request.Cookies[RefreshCookie], ct);
            http.Response.Cookies.Delete(RefreshCookie, new CookieOptions { Path = CookiePath });
            return Results.NoContent();
        });
    }

    private static LoginResponse Respond(HttpContext http, AuthResult result)
    {
        // The refresh token only ever travels in this cookie. HttpOnly keeps scripts from reading it,
        // SameSite=Strict keeps other sites from sending it, and the path keeps it off every other request.
        http.Response.Cookies.Append(RefreshCookie, result.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = !http.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment(),
            SameSite = SameSiteMode.Strict,
            Path = CookiePath,
            Expires = result.RefreshTokenExpiresAt,
        });
        return new LoginResponse(result.AccessToken.Token, result.AccessToken.ExpiresAt, result.User);
    }
}
