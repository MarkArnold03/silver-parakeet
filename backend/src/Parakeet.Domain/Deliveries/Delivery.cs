using Parakeet.Domain.Common;
using Parakeet.Domain.Orders;

namespace Parakeet.Domain.Deliveries;

/// <summary>The logistics record for an order: slot, route, status. Every change goes through a method here.</summary>
public class Delivery
{
    private readonly List<DeliveryEvent> _events = [];

    private Delivery() { }

    public Delivery(Order order)
    {
        Id = Guid.CreateVersion7();
        OrderId = order.Id;
        Status = DeliveryStatus.AwaitingBooking;
        // Copied once. Order lines don't change after creation, and sums over deliveries stay a simple query.
        VolumeM3 = order.TotalVolumeM3;
        WeightKg = order.TotalWeightKg;
    }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public DeliveryStatus Status { get; private set; }
    public Guid? SlotId { get; private set; }
    public Guid? RouteId { get; private set; }
    public int? StopSequence { get; private set; }
    public decimal VolumeM3 { get; private set; }
    public decimal WeightKg { get; private set; }
    public AlternativeRecipient? AlternativeRecipient { get; private set; }
    public string? FailureReason { get; private set; }
    /// <summary>Part of the customer link. Bumping it makes every older link stop working.</summary>
    public int LinkVersion { get; private set; } = 1;
    /// <summary>Set by SQL Server on every update. Stops two people overwriting each other's change.</summary>
    public byte[] RowVersion { get; private set; } = [];
    public IReadOnlyCollection<DeliveryEvent> Events => _events;

    /// <summary>
    /// Book a slot, or move to another one. Only the status rule lives here. Capacity and cutoff are
    /// checked by the booking service, which needs data from other tables.
    /// </summary>
    public void Book(Guid slotId, Actor actor, DateTimeOffset now)
    {
        if (!DeliveryStatusRules.CanBook(Status))
            throw new ConflictException("RebookNotAllowed", $"A delivery that is {Status} can't be booked or rebooked.");
        if (SlotId == slotId && Status is DeliveryStatus.Booked or DeliveryStatus.Planned)
            return;

        if (Status == DeliveryStatus.Planned)
            Unassign(actor, "Rebooked", now);

        var from = Status;
        SlotId = slotId;
        Status = DeliveryStatus.Booked;
        FailureReason = null;
        var type = from == DeliveryStatus.AwaitingBooking ? DeliveryEventType.StatusChanged : DeliveryEventType.Rebooked;
        AddEvent(type, from, actor, null, now);
    }

    public void AssignToRoute(Guid routeId, int stopSequence, Actor actor, DateTimeOffset now)
    {
        if (!DeliveryStatusRules.CanAssign(Status))
            throw new ConflictException("AssignNotAllowed", $"A delivery that is {Status} can't be put on a route.");

        var from = Status;
        RouteId = routeId;
        StopSequence = stopSequence;
        Status = DeliveryStatus.Planned;
        AddEvent(DeliveryEventType.Assigned, from, actor, null, now);
    }

    public void Unassign(Actor actor, string? reason, DateTimeOffset now)
    {
        if (Status != DeliveryStatus.Planned)
            throw new ConflictException("UnassignNotAllowed", $"A delivery that is {Status} can't be taken off its route.");

        RouteId = null;
        StopSequence = null;
        Status = DeliveryStatus.Booked;
        AddEvent(DeliveryEventType.Unassigned, DeliveryStatus.Planned, actor, reason, now);
    }

    public void SetStopSequence(int stopSequence)
    {
        if (RouteId is null)
            throw new ConflictException("NotOnRoute", "Only a delivery on a route has a stop number.");
        StopSequence = stopSequence;
    }

    /// <summary>The single status endpoint ends up here: Loaded, OutForDelivery, Delivered, Failed.</summary>
    public void ChangeStatus(DeliveryStatus to, Actor actor, string? reason, DateTimeOffset now)
    {
        if (!DeliveryStatusRules.CanChangeStatus(Status, to))
            throw new ConflictException("IllegalTransition", $"A delivery can't go from {Status} to {to}.");

        var from = Status;
        if (to == DeliveryStatus.Failed)
        {
            FailureReason = Guard.Required(reason, "Reason", 500);
            // Back to the unplanned list. The slot is kept so we can see when the attempt was made.
            RouteId = null;
            StopSequence = null;
        }
        Status = to;
        AddEvent(DeliveryEventType.StatusChanged, from, actor, Guard.Optional(reason, "Reason", 500), now);
    }

    public void SetAlternativeRecipient(AlternativeRecipient? recipient) => AlternativeRecipient = recipient;

    /// <summary>Revokes every customer link issued so far.</summary>
    public void RevokeLinks() => LinkVersion++;

    private void AddEvent(DeliveryEventType type, DeliveryStatus from, Actor actor, string? reason, DateTimeOffset at)
        => _events.Add(new DeliveryEvent(Id, type, from, Status, actor, reason, at));
}
