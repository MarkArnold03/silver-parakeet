using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Parakeet.Domain.Common;
using Parakeet.Domain.Merchants;

namespace Parakeet.Application.Merchants;

public record MerchantRequest(string Name);
public record MerchantDto(Guid Id, string Name, string ApiKeyPrefix, bool IsActive);
/// <summary>The only time the full key is ever shown. We keep just its hash.</summary>
public record MerchantWithKeyDto(MerchantDto Merchant, string ApiKey);

public class MerchantService(IAppDbContext db)
{
    private const int PrefixLength = 10;

    /// <summary>The merchant the key belongs to, or 401.</summary>
    public async Task<Guid> AuthenticateAsync(string? apiKey, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            var hash = HashKey(apiKey.Trim());
            var merchant = await db.Merchants.AsNoTracking().SingleOrDefaultAsync(m => m.ApiKeyHash == hash, ct);
            if (merchant is { IsActive: true })
                return merchant.Id;
        }
        throw new UnauthorizedException("InvalidApiKey", "The API key is missing or wrong.");
    }

    public async Task<IReadOnlyList<MerchantDto>> ListAsync(CancellationToken ct = default)
        => await db.Merchants.AsNoTracking().OrderBy(m => m.Name).Select(m => ToDto(m)).ToListAsync(ct);

    public async Task<MerchantWithKeyDto> CreateAsync(MerchantRequest request, CancellationToken ct = default)
    {
        var key = NewKey();
        var merchant = new Merchant(request.Name, HashKey(key), key[..PrefixLength]);
        db.Merchants.Add(merchant);
        await db.SaveChangesAsync(ct);
        return new MerchantWithKeyDto(ToDto(merchant), key);
    }

    /// <summary>Replaces the key. The old one stops working at once.</summary>
    public async Task<MerchantWithKeyDto> NewApiKeyAsync(Guid id, CancellationToken ct = default)
    {
        var merchant = await db.Merchants.SingleOrDefaultAsync(m => m.Id == id, ct)
            ?? throw new NotFoundException("MerchantNotFound", "The merchant doesn't exist.");
        var key = NewKey();
        merchant.SetApiKey(HashKey(key), key[..PrefixLength]);
        await db.SaveChangesAsync(ct);
        return new MerchantWithKeyDto(ToDto(merchant), key);
    }

    // A plain SHA-256 is enough here (unlike for passwords): the key is 32 random bytes, so it can't be guessed.
    public static string HashKey(string apiKey) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)));

    private static string NewKey() => "pk_" + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    private static MerchantDto ToDto(Merchant m) => new(m.Id, m.Name, m.ApiKeyPrefix, m.IsActive);
}
