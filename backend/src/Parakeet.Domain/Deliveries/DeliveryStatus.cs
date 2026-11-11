namespace Parakeet.Domain.Deliveries;

public enum DeliveryStatus
{
    /// <summary>The order has arrived from the merchant but the customer hasn't picked a slot.</summary>
    AwaitingBooking,
    Booked,
    /// <summary>Assigned to a truck route.</summary>
    Planned,
    Loaded,
    OutForDelivery,
    Delivered,
    /// <summary>The attempt failed. The delivery is back in the unplanned list and can be rebooked.</summary>
    Failed,
}

/// <summary>The status machine as pure functions, so it can be tested without a database.</summary>
public static class DeliveryStatusRules
{
    // What the single status endpoint may do. Booking, assigning and unassigning have their own methods.
    private static readonly HashSet<(DeliveryStatus From, DeliveryStatus To)> StatusChanges =
    [
        (DeliveryStatus.Planned, DeliveryStatus.Loaded),
        (DeliveryStatus.Loaded, DeliveryStatus.OutForDelivery),
        (DeliveryStatus.OutForDelivery, DeliveryStatus.Delivered),
        (DeliveryStatus.OutForDelivery, DeliveryStatus.Failed),
    ];

    public static bool CanChangeStatus(DeliveryStatus from, DeliveryStatus to) => StatusChanges.Contains((from, to));

    public static bool CanBook(DeliveryStatus status) => status is
        DeliveryStatus.AwaitingBooking or DeliveryStatus.Booked or DeliveryStatus.Planned or DeliveryStatus.Failed;

    /// <summary>
    /// Booked and Planned deliveries can only be moved while their current slot is before its cutoff.
    /// AwaitingBooking has no slot and a Failed delivery's slot is already in the past, so they skip that check.
    /// </summary>
    public static bool RebookNeedsCurrentSlotBeforeCutoff(DeliveryStatus status) => status is
        DeliveryStatus.Booked or DeliveryStatus.Planned;

    public static bool CanAssign(DeliveryStatus status) => status is DeliveryStatus.Booked or DeliveryStatus.Planned;
}
