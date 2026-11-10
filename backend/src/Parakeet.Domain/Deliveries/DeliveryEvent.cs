namespace Parakeet.Domain.Deliveries;

public enum ActorType { Dispatcher, Driver, System, Customer }

/// <summary>Who did something. Id is the user id for dispatchers and drivers, null for system and customer.</summary>
public record Actor(ActorType Type, string? Id = null)
{
    public static readonly Actor System = new(ActorType.System);
    public static readonly Actor Customer = new(ActorType.Customer);
}

public enum DeliveryEventType { StatusChanged, Rebooked, Assigned, Unassigned, CapacityOverride, AddressFixed }

/// <summary>One row per thing that happened to a delivery. Tracking timeline and audit log in one table.</summary>
public class DeliveryEvent
{
    private DeliveryEvent() { }

    internal DeliveryEvent(Guid deliveryId, DeliveryEventType type, DeliveryStatus? from, DeliveryStatus? to,
        Actor actor, string? reason, DateTimeOffset at)
    {
        Id = Guid.CreateVersion7();
        DeliveryId = deliveryId;
        Type = type;
        FromStatus = from;
        ToStatus = to;
        ActorType = actor.Type;
        ActorId = actor.Id;
        Reason = reason;
        At = at;
    }

    public Guid Id { get; private set; }
    public Guid DeliveryId { get; private set; }
    public DeliveryEventType Type { get; private set; }
    public DeliveryStatus? FromStatus { get; private set; }
    public DeliveryStatus? ToStatus { get; private set; }
    public ActorType ActorType { get; private set; }
    public string? ActorId { get; private set; }
    public string? Reason { get; private set; }
    public DateTimeOffset At { get; private set; }
}
