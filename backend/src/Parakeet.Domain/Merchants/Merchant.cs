using Parakeet.Domain.Common;

namespace Parakeet.Domain.Merchants;

public class Merchant
{
    private Merchant() { }

    public Merchant(string name, string apiKeyHash, string apiKeyPrefix)
    {
        Id = Guid.CreateVersion7();
        Name = Guard.Required(name, nameof(Name), 200);
        SetApiKey(apiKeyHash, apiKeyPrefix);
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    /// <summary>Only the hash is stored. The key itself is shown once when it is created.</summary>
    public string ApiKeyHash { get; private set; } = null!;
    /// <summary>First characters of the key, shown in the UI to tell keys apart.</summary>
    public string ApiKeyPrefix { get; private set; } = null!;
    public bool IsActive { get; private set; } = true;

    public void SetApiKey(string apiKeyHash, string apiKeyPrefix)
    {
        ApiKeyHash = Guard.Required(apiKeyHash, nameof(ApiKeyHash), 128);
        ApiKeyPrefix = Guard.Required(apiKeyPrefix, nameof(ApiKeyPrefix), 16);
    }

    public void Deactivate() => IsActive = false;
}
