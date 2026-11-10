using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Parakeet.Domain.Common;
using Parakeet.Domain.Users;

namespace Parakeet.Application.Auth;

public record AccessToken(string Token, DateTimeOffset ExpiresAt);
public record UserDto(Guid Id, string Email, string Name, UserRole Role);
/// <summary>RefreshToken is the raw secret. It goes into the cookie and is never stored or logged.</summary>
public record AuthResult(AccessToken AccessToken, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt, UserDto User);

public interface IAccessTokenIssuer
{
    AccessToken Issue(User user);
}

public class AuthService(
    IAppDbContext db,
    IPasswordHasher<User> passwordHasher,
    IAccessTokenIssuer tokens,
    TimeProvider time,
    IOptions<AuthOptions> options)
{
    public async Task<AuthResult> LoginAsync(string? email, string? password, CancellationToken ct = default)
    {
        var normalized = (email ?? "").Trim().ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == normalized, ct);

        var ok = user is not null && password is not null
            && passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password) != PasswordVerificationResult.Failed;
        if (user is null)
            passwordHasher.HashPassword(null!, password ?? ""); // same work as a real check, so timing doesn't reveal which emails exist
        if (!ok)
            throw new UnauthorizedException("InvalidCredentials", "Wrong email or password.");

        var result = AddRefreshToken(user!, out _);
        await db.SaveChangesAsync(ct);
        return result;
    }

    /// <summary>Swaps a refresh token for a new one plus a new access token. Each token works once.</summary>
    public async Task<AuthResult> RefreshAsync(string? refreshToken, CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var token = await FindAsync(refreshToken, ct) ?? throw Invalid();

        if (token.RevokedAt is not null)
        {
            // A used token came back: someone has a copy. End every session that grew out of it.
            for (var next = token; next?.ReplacedById is { } id;)
            {
                next = await db.RefreshTokens.SingleOrDefaultAsync(t => t.Id == id, ct);
                next?.Revoke(now);
            }
            await db.SaveChangesAsync(ct);
            throw new UnauthorizedException("RefreshTokenReused", "Please log in again.");
        }
        if (token.ExpiresAt <= now)
            throw Invalid();

        var user = await db.Users.SingleAsync(u => u.Id == token.UserId, ct);
        var result = AddRefreshToken(user, out var replacement);
        token.Revoke(now, replacement.Id);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Two requests used the same token at the same moment. Only the first one wins.
            throw Invalid();
        }
        return result;
    }

    public async Task LogoutAsync(string? refreshToken, CancellationToken ct = default)
    {
        if (await FindAsync(refreshToken, ct) is { } token)
        {
            token.Revoke(time.GetUtcNow());
            await db.SaveChangesAsync(ct);
        }
    }

    // ponytail: used tokens are never deleted. One small row per login or refresh. Add a cleanup job when the table matters.
    private AuthResult AddRefreshToken(User user, out RefreshToken stored)
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var expiresAt = time.GetUtcNow().AddDays(options.Value.RefreshTokenDays);
        stored = new RefreshToken(user.Id, Hash(raw), expiresAt);
        db.RefreshTokens.Add(stored);
        return new AuthResult(tokens.Issue(user), raw, expiresAt, new UserDto(user.Id, user.Email, user.Name, user.Role));
    }

    private async Task<RefreshToken?> FindAsync(string? raw, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        var hash = Hash(raw);
        return await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
    }

    // Only the hash is stored, so a leaked database can't be used to log in. Plain SHA-256 is enough
    // here because the token is 256 random bits, unlike a password.
    private static string Hash(string raw) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

    private static UnauthorizedException Invalid() => new("InvalidRefreshToken", "Please log in again.");
}
