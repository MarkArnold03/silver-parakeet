using Parakeet.Domain.Common;

namespace Parakeet.Domain.Orders;

public enum DeliveryType { Curbside, Doorstep }

/// <summary>The merchant's data: who, where and what. The logistics side lives on Delivery.</summary>
public class Order
{
    private readonly List<OrderLine> _lines = [];

    private Order() { }

    public Order(
        Guid merchantId,
        string merchantOrderRef,
        Recipient recipient,
        Address address,
        DeliveryType deliveryType,
        IEnumerable<OrderLine> lines,
        string payloadHash,
        DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        MerchantId = merchantId;
        MerchantOrderRef = Guard.Required(merchantOrderRef, nameof(MerchantOrderRef), 100);
        Recipient = recipient;
        Address = address;
        DeliveryType = deliveryType;
        PayloadHash = Guard.Required(payloadHash, nameof(PayloadHash), 64);
        CreatedAt = now;

        _lines.AddRange(lines);
        if (_lines.Count == 0)
            throw new DomainException("NoLines", "An order needs at least one line.");
    }

    public Guid Id { get; private set; }
    public Guid MerchantId { get; private set; }
    /// <summary>The merchant's own reference. Unique per merchant, which makes a retried POST safe.</summary>
    public string MerchantOrderRef { get; private set; } = null!;
    /// <summary>Our number, shown to the customer. Assigned by a database sequence on insert.</summary>
    public long OrderNumber { get; private set; }
    public Recipient Recipient { get; private set; } = null!;
    public Address Address { get; private set; } = null!;
    public DeliveryType DeliveryType { get; private set; }
    /// <summary>SHA-256 of the create request, to tell a retry from a different order reusing the ref.</summary>
    public string PayloadHash { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public IReadOnlyCollection<OrderLine> Lines => _lines;

    // Totals are always calculated from the lines, never typed in.
    public decimal TotalVolumeM3 => _lines.Sum(l => l.VolumeM3);
    public decimal TotalWeightKg => _lines.Sum(l => l.WeightKg);

    public void ChangeAddress(Address address) => Address = address;
}
