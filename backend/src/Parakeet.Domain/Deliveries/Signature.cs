using Parakeet.Domain.Common;

namespace Parakeet.Domain.Deliveries;

public enum SignatureKind { Delivery, Waiver }

public class Signature
{
    private Signature() { }

    public Signature(Guid deliveryId, SignatureKind kind, string signerName, string svg, DateTimeOffset signedAt)
    {
        Id = Guid.CreateVersion7();
        DeliveryId = deliveryId;
        Kind = kind;
        SignerName = Guard.Required(signerName, nameof(SignerName), 200);
        Svg = string.IsNullOrWhiteSpace(svg) ? throw new DomainException("Required", "Signature is required.") : svg;
        SignedAt = signedAt;
    }

    public Guid Id { get; private set; }
    public Guid DeliveryId { get; private set; }
    public SignatureKind Kind { get; private set; }
    public string SignerName { get; private set; } = null!;
    public string Svg { get; private set; } = null!;
    public DateTimeOffset SignedAt { get; private set; }
}
