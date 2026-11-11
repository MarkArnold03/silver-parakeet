using Parakeet.Domain.Common;

namespace Parakeet.Domain.Users;

public enum UserRole { Dispatcher, Admin }

/// <summary>Someone who logs in to the dispatch portal.</summary>
public class User
{
    private User() { }

    public User(string email, string name, UserRole role)
    {
        Id = Guid.CreateVersion7();
        Email = Guard.Required(email, nameof(Email), 254).ToLowerInvariant();
        Name = Guard.Required(name, nameof(Name), 200);
        Role = role;
    }

    public Guid Id { get; private set; }
    public string Email { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string PasswordHash { get; private set; } = "";
    public UserRole Role { get; private set; }

    public void SetPasswordHash(string hash) => PasswordHash = Guard.Required(hash, nameof(PasswordHash), 500);
}

/// <summary>A long-lived login token. Stored hashed, replaced on every use.</summary>
public class RefreshToken
{
    private RefreshToken() { }

    public RefreshToken(Guid userId, string tokenHash, DateTimeOffset expiresAt)
    {
        Id = Guid.CreateVersion7();
        UserId = userId;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = null!;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public Guid? ReplacedById { get; private set; }

    public void Revoke(DateTimeOffset now, Guid? replacedById = null)
    {
        RevokedAt ??= now;
        ReplacedById ??= replacedById;
    }
}
