using Parakeet.Domain.Common;

namespace Parakeet.Domain.Payments;

/// <summary>A row in the price list: carry-in, assembly, packaging removal.</summary>
public class ExtraService
{
    private ExtraService() { }

    public ExtraService(string code, string nameSv, string nameEn, long priceOre)
    {
        Id = Guid.CreateVersion7();
        Code = Guard.Required(code, nameof(Code), 50);
        Update(nameSv, nameEn, priceOre);
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = null!;
    public string NameSv { get; private set; } = null!;
    public string NameEn { get; private set; } = null!;
    /// <summary>Money is whole öre in a long (19900 = 199 kr), so there is never a rounding error.</summary>
    public long PriceOre { get; private set; }
    public bool IsActive { get; private set; } = true;

    public void Update(string nameSv, string nameEn, long priceOre)
    {
        NameSv = Guard.Required(nameSv, nameof(NameSv), 100);
        NameEn = Guard.Required(nameEn, nameof(NameEn), 100);
        PriceOre = priceOre >= 0 ? priceOre : throw new DomainException("Negative", "Price can't be negative.");
    }

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
}
