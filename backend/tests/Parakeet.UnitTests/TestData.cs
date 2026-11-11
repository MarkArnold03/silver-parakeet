using Parakeet.Domain.Deliveries;
using Parakeet.Domain.Orders;

namespace Parakeet.UnitTests;

internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
    public static readonly Actor Dispatcher = new(ActorType.Dispatcher, "user-1");

    public static Order Order(params OrderLine[] lines) => new(
        Guid.NewGuid(), "REF-1",
        new Recipient("Anna Andersson", "+46701234567", "anna@example.com"),
        new Address("Storgatan 1", "114 55", "Stockholm"),
        DeliveryType.Curbside,
        lines.Length > 0 ? lines : [new OrderLine("A-1", "Sofa", 1, 1.5m, 60m)],
        new string('a', 64), Now);

    public static Delivery Delivery() => new(Order());

    /// <summary>A delivery driven through the real methods until it has the wanted status.</summary>
    public static Delivery DeliveryIn(DeliveryStatus status)
    {
        var d = Delivery();
        if (status == DeliveryStatus.AwaitingBooking) return d;
        d.Book(Guid.NewGuid(), Actor.Customer, Now);
        if (status == DeliveryStatus.Booked) return d;
        d.AssignToRoute(Guid.NewGuid(), 1, Dispatcher, Now);
        if (status == DeliveryStatus.Planned) return d;
        d.ChangeStatus(DeliveryStatus.Loaded, Dispatcher, null, Now);
        if (status == DeliveryStatus.Loaded) return d;
        d.ChangeStatus(DeliveryStatus.OutForDelivery, Dispatcher, null, Now);
        if (status == DeliveryStatus.OutForDelivery) return d;
        if (status == DeliveryStatus.Delivered) d.ChangeStatus(DeliveryStatus.Delivered, Dispatcher, null, Now);
        else d.ChangeStatus(DeliveryStatus.Failed, Dispatcher, "Nobody home", Now);
        return d;
    }
}
