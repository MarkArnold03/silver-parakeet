using Parakeet.Domain.Common;

namespace Parakeet.Domain.Payments;

public enum PaymentStatus { Pending, Paid, Expired }

public class Payment
{
    private Payment() { }

    public Payment(Guid deliveryId, long amountOre, string stripeCheckoutSessionId)
    {
        Id = Guid.CreateVersion7();
        DeliveryId = deliveryId;
        AmountOre = amountOre > 0 ? amountOre : throw new DomainException("NotPositive", "Amount must be positive.");
        StripeCheckoutSessionId = Guard.Required(stripeCheckoutSessionId, nameof(StripeCheckoutSessionId), 255);
    }

    public Guid Id { get; private set; }
    public Guid DeliveryId { get; private set; }
    public long AmountOre { get; private set; }
    public string StripeCheckoutSessionId { get; private set; } = null!;
    public PaymentStatus Status { get; private set; } = PaymentStatus.Pending;

    public void MarkPaid() => Status = PaymentStatus.Paid;
    public void MarkExpired() => Status = PaymentStatus.Expired;
}

/// <summary>Stripe can send the same webhook event twice. Inserting its id first makes the second one a no-op.</summary>
public class ProcessedStripeEvent(string eventId)
{
    public string EventId { get; private set; } = eventId;
}
