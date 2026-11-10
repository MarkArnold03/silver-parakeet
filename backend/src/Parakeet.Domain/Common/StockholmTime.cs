namespace Parakeet.Domain.Common;

/// <summary>Slots and cutoffs are in Swedish local time. Timestamps are stored in UTC and converted here.</summary>
public static class StockholmTime
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm");

    public static DateTime ToLocal(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, Zone).DateTime;
    public static DateOnly Today(DateTimeOffset instant) => DateOnly.FromDateTime(ToLocal(instant));
}
