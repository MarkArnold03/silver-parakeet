namespace Parakeet.Domain.Payments;

public enum DeliveryExtraStatus { Unpaid, Paid }

/// <summary>An extra service added to one delivery, with the price as it was when it was added.</summary>
public class DeliveryExtra
{
    private DeliveryExtra() { }

    public DeliveryExtra(Guid deliveryId, ExtraService service)
    {
        Id = Guid.CreateVersion7();
        DeliveryId = deliveryId;
        ExtraServiceId = service.Id;
        PriceOre = service.PriceOre;
    }

    public Guid Id { get; private set; }
    public Guid DeliveryId { get; private set; }
    public Guid ExtraServiceId { get; private set; }
    public long PriceOre { get; private set; }
    public DeliveryExtraStatus Status { get; private set; } = DeliveryExtraStatus.Unpaid;
    public Guid? PaymentId { get; private set; }

    public void AttachToPayment(Guid paymentId) => PaymentId = paymentId;
    public void MarkPaid() => Status = DeliveryExtraStatus.Paid;
}
