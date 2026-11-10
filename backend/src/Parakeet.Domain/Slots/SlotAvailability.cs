namespace Parakeet.Domain.Slots;

/// <summary>Settings that decide what can be booked. Filled from configuration.</summary>
/// <param name="SellableVolumeRatio">Share of truck m3 we sell (0.85). Real loads never pack perfectly.</param>
/// <param name="CutoffTime">Booking for a date closes at this time the day before.</param>
public record BookingRules(decimal SellableVolumeRatio, TimeOnly CutoffTime);

/// <summary>One zone on one date: what its trucks can carry and what is already promised.</summary>
public record ZoneDayLoad(decimal TruckM3, decimal TruckKg, decimal ReservedM3, decimal ReservedKg)
{
    public static readonly ZoneDayLoad Empty = new(0, 0, 0, 0);

    public decimal SellableM3(BookingRules rules) => TruckM3 * rules.SellableVolumeRatio;
    public decimal SellableKg => TruckKg; // weight is sold to 100%

    /// <summary>True after a truck was removed and more is promised than the remaining trucks can take.</summary>
    public bool IsOversold(BookingRules rules) => ReservedM3 > SellableM3(rules) || ReservedKg > SellableKg;
}

public static class BookingErrors
{
    public const string WrongZone = "WrongZone";
    public const string SlotClosed = "SlotClosed";
    public const string CutoffPassed = "CutoffPassed";
    public const string SlotFull = "SlotFull";
    public const string ZoneDayFull = "ZoneDayFull";
}

/// <summary>The booking rule. Pure: everything it needs is passed in, so every case can be unit tested.</summary>
public static class SlotAvailability
{
    public static DateTime Cutoff(DateOnly slotDate, BookingRules rules)
        => slotDate.AddDays(-1).ToDateTime(rules.CutoffTime);

    public static bool IsBeforeCutoff(DateOnly slotDate, DateTime localNow, BookingRules rules)
        => localNow < Cutoff(slotDate, rules);

    /// <summary>Returns null when the delivery can book the slot, otherwise one of <see cref="BookingErrors"/>.</summary>
    /// <param name="bookedCount">Deliveries already in this slot, not counting the one being booked.</param>
    /// <param name="load">The slot's zone-day, not counting the delivery being booked.</param>
    public static string? WhyNotBookable(
        Slot slot, Guid? deliveryZoneId, int bookedCount, ZoneDayLoad load,
        decimal volumeM3, decimal weightKg, DateTime localNow, BookingRules rules)
    {
        if (deliveryZoneId != slot.ZoneId) return BookingErrors.WrongZone;
        if (slot.IsClosed) return BookingErrors.SlotClosed;
        if (!IsBeforeCutoff(slot.Date, localNow, rules)) return BookingErrors.CutoffPassed;
        if (bookedCount >= slot.MaxDeliveries) return BookingErrors.SlotFull;
        if (load.ReservedM3 + volumeM3 > load.SellableM3(rules)) return BookingErrors.ZoneDayFull;
        if (load.ReservedKg + weightKg > load.SellableKg) return BookingErrors.ZoneDayFull;
        return null;
    }
}
