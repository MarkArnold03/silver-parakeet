using Parakeet.Domain.Common;
using Parakeet.Domain.Slots;

namespace Parakeet.UnitTests;

public class SlotAvailabilityTests
{
    private static readonly BookingRules Rules = new(0.85m, new TimeOnly(18, 0));
    private static readonly Guid Zone = Guid.NewGuid();
    private static readonly DateOnly Date = new(2026, 11, 12);
    private static readonly DateTime WellBefore = new(2026, 11, 10, 9, 0, 0);
    // One truck of 20 m3 and 1000 kg: 17 m3 (85%) and 1000 kg can be sold.
    private static readonly ZoneDayLoad OneTruck = new(20m, 1000m, 0m, 0m);

    private static Slot Slot(int max = 8) => new(Zone, Date, new TimeOnly(8, 0), new TimeOnly(12, 0), max);

    private static string? Check(Slot? slot = null, Guid? deliveryZone = null, int booked = 0, ZoneDayLoad? load = null,
        decimal m3 = 1m, decimal kg = 50m, DateTime? now = null)
        => SlotAvailability.WhyNotBookable(slot ?? Slot(), deliveryZone ?? Zone, booked, load ?? OneTruck, m3, kg,
            now ?? WellBefore, Rules);

    [Fact]
    public void A_free_slot_with_room_on_the_trucks_is_bookable() => Assert.Null(Check());

    [Theory]
    [InlineData(7, null)]
    [InlineData(8, BookingErrors.SlotFull)]
    [InlineData(9, BookingErrors.SlotFull)] // max was lowered after bookings were made
    public void Delivery_count_is_limited_per_slot(int booked, string? expected)
        => Assert.Equal(expected, Check(Slot(max: 8), booked: booked));

    [Theory]
    [InlineData(16.0, 1.0, null)]                       // lands exactly on 17 m3 = 85%
    [InlineData(16.0, 1.001, BookingErrors.ZoneDayFull)] // one litre over
    [InlineData(0.0, 17.0, null)]
    [InlineData(0.0, 20.0, BookingErrors.ZoneDayFull)]   // fits the truck but not the 85% we sell
    public void Volume_is_sold_up_to_85_percent(double reserved, double volume, string? expected)
        => Assert.Equal(expected, Check(load: OneTruck with { ReservedM3 = (decimal)reserved }, m3: (decimal)volume));

    [Theory]
    [InlineData(950, 50, null)]                         // lands exactly on 1000 kg = 100%
    [InlineData(950, 50.01, BookingErrors.ZoneDayFull)]
    [InlineData(0, 1000, null)]
    public void Weight_is_sold_up_to_100_percent(double reserved, double weight, string? expected)
        => Assert.Equal(expected, Check(load: OneTruck with { ReservedKg = (decimal)reserved }, kg: (decimal)weight));

    [Fact]
    public void Capacity_is_the_sum_of_the_trucks_in_the_zone_that_day()
    {
        var twoTrucks = new ZoneDayLoad(40m, 2000m, 30m, 0m); // 34 m3 sellable, 30 taken
        Assert.Null(Check(load: twoTrucks, m3: 4m));
        Assert.Equal(BookingErrors.ZoneDayFull, Check(load: twoTrucks, m3: 4.5m));
    }

    [Fact]
    public void No_trucks_means_nothing_can_be_booked()
        => Assert.Equal(BookingErrors.ZoneDayFull, Check(load: ZoneDayLoad.Empty));

    [Fact]
    public void A_closed_slot_is_not_bookable()
    {
        var slot = Slot();
        slot.Close();
        Assert.Equal(BookingErrors.SlotClosed, Check(slot));
        slot.Reopen();
        Assert.Null(Check(slot));
    }

    [Theory]
    [InlineData("2026-11-11T17:59:59", null)]
    [InlineData("2026-11-11T18:00:00", BookingErrors.CutoffPassed)] // 18:00 the day before, sharp
    [InlineData("2026-11-12T07:00:00", BookingErrors.CutoffPassed)]
    public void Booking_closes_at_the_cutoff_the_day_before(string now, string? expected)
        => Assert.Equal(expected, Check(now: DateTime.Parse(now)));

    [Fact]
    public void A_slot_in_another_zone_is_refused()
    {
        Assert.Equal(BookingErrors.WrongZone, Check(deliveryZone: Guid.NewGuid()));
        // An address outside every zone has no zone at all.
        Assert.Equal(BookingErrors.WrongZone,
            SlotAvailability.WhyNotBookable(Slot(), null, 0, OneTruck, 1m, 1m, WellBefore, Rules));
    }

    [Fact]
    public void Oversold_shows_when_a_truck_was_removed_after_bookings()
    {
        Assert.False(new ZoneDayLoad(20m, 1000m, 17m, 1000m).IsOversold(Rules));
        Assert.True(new ZoneDayLoad(20m, 1000m, 17.001m, 0m).IsOversold(Rules));
        Assert.True(new ZoneDayLoad(0m, 0m, 1m, 10m).IsOversold(Rules));
    }

    [Theory]
    [InlineData("2026-03-28T23:30:00+00:00", "2026-03-29T00:30:00")] // winter time, UTC+1
    [InlineData("2026-06-30T22:30:00+00:00", "2026-07-01T00:30:00")] // summer time, UTC+2
    public void Utc_is_converted_to_swedish_local_time(string utc, string local)
        => Assert.Equal(DateTime.Parse(local), StockholmTime.ToLocal(DateTimeOffset.Parse(utc)));
}
