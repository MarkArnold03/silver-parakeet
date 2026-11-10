using Parakeet.Domain.Slots;

namespace Parakeet.Application.Booking;

/// <summary>Section "Booking" in appsettings. Every value can be overridden with an environment variable.</summary>
public class BookingOptions
{
    public decimal SellableVolumeRatio { get; set; } = 0.85m;
    /// <summary>Booking and rebooking for a date closes at this time the day before (Swedish time).</summary>
    public TimeOnly CutoffTime { get; set; } = new(18, 0);
    public int LinkValidityDaysAfterDelivery { get; set; } = 7;
    public decimal TruckFillWarnRatio { get; set; } = 0.90m;

    public BookingRules ToRules() => new(SellableVolumeRatio, CutoffTime);
}
