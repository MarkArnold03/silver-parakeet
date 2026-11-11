using Microsoft.EntityFrameworkCore;
using Parakeet.Application.Slots;
using Parakeet.Domain.Deliveries;
using Parakeet.Domain.Routes;
using Parakeet.Domain.Slots;
using static Parakeet.IntegrationTests.Scenario;

namespace Parakeet.IntegrationTests;

[Collection(SqlServerCollection.Name)]
public class BookingTests(SqlServerFixture sql)
{
    [Fact]
    public async Task Eight_customers_race_for_the_last_place_in_a_slot_and_exactly_one_wins()
    {
        var s = await CreateAsync(sql);
        await s.AddTruckAsync(Day1, m3: 100m, kg: 10000m);
        var slot = await s.AddSlotAsync(Day1, max: 1);
        var deliveries = new List<Delivery>();
        for (var i = 0; i < 8; i++) deliveries.Add(await s.AddDeliveryAsync());

        var results = await Task.WhenAll(deliveries.Select(d => Task.Run(() => s.TryBookAsync(d, slot))));

        Assert.Single(results, r => r is null);
        Assert.Equal(7, results.Count(r => r == BookingErrors.SlotFull));
        await using var db = s.Db();
        Assert.Equal(1, await db.Deliveries.CountAsync(d => d.SlotId == slot.Id && d.Status == DeliveryStatus.Booked));
    }

    [Fact]
    public async Task Eight_customers_race_for_the_last_truck_space_across_two_slots_and_exactly_one_wins()
    {
        var s = await CreateAsync(sql);
        await s.AddTruckAsync(Day1, m3: 10m); // 8.5 m3 can be sold, so only one 5 m3 delivery fits
        var morning = await s.AddSlotAsync(Day1, startHour: 8, max: 100);
        var afternoon = await s.AddSlotAsync(Day1, startHour: 12, max: 100);
        var deliveries = new List<Delivery>();
        for (var i = 0; i < 8; i++) deliveries.Add(await s.AddDeliveryAsync(m3: 5m));

        // The slots differ but the trucks are shared by the whole day, so the lock must be per zone-day.
        var results = await Task.WhenAll(deliveries.Select((d, i) =>
            Task.Run(() => s.TryBookAsync(d, i % 2 == 0 ? morning : afternoon))));

        Assert.Single(results, r => r is null);
        Assert.Equal(7, results.Count(r => r == BookingErrors.ZoneDayFull));
        Assert.Equal(5m, (await s.ZoneDayAsync(Day1)).ReservedM3);
    }

    [Fact]
    public async Task Rebooking_gives_the_old_day_its_space_back()
    {
        var s = await CreateAsync(sql);
        await s.AddTruckAsync(Day1, m3: 10m);
        await s.AddTruckAsync(Day2, m3: 10m);
        Slot day1 = await s.AddSlotAsync(Day1), day2 = await s.AddSlotAsync(Day2);
        Delivery first = await s.AddDeliveryAsync(m3: 5m), second = await s.AddDeliveryAsync(m3: 5m);

        await s.BookAsync(first, day1);
        Assert.Equal(BookingErrors.ZoneDayFull, await s.TryBookAsync(second, day1));

        await s.BookAsync(first, day2);

        Assert.Null(await s.TryBookAsync(second, day1));
        Assert.Equal(5m, (await s.ZoneDayAsync(Day1)).ReservedM3);
        Assert.Equal(5m, (await s.ZoneDayAsync(Day2)).ReservedM3);
        var moved = await s.ReloadAsync(first);
        Assert.Equal(day2.Id, moved.SlotId);
        Assert.Equal([DeliveryEventType.StatusChanged, DeliveryEventType.Rebooked], moved.Events.OrderBy(e => e.Id).Select(e => e.Type));
    }

    [Fact]
    public async Task Moving_to_another_slot_the_same_day_does_not_compete_with_itself()
    {
        var s = await CreateAsync(sql);
        await s.AddTruckAsync(Day1, m3: 10m);
        Slot morning = await s.AddSlotAsync(Day1, 8), afternoon = await s.AddSlotAsync(Day1, 12);
        var delivery = await s.AddDeliveryAsync(m3: 5m);
        await s.BookAsync(delivery, morning);

        // Its own 5 m3 must not be counted against it, both when listing and when booking.
        Assert.Equal([morning.Id, afternoon.Id], (await s.AvailableAsync(delivery)).Select(a => a.Id));
        await s.BookAsync(delivery, afternoon);

        Assert.Equal(afternoon.Id, (await s.ReloadAsync(delivery)).SlotId);
        Assert.Equal(5m, (await s.ZoneDayAsync(Day1)).ReservedM3);
    }

    [Fact]
    public async Task Two_deliveries_swapping_days_at_the_same_time_do_not_deadlock()
    {
        var s = await CreateAsync(sql);
        await s.AddTruckAsync(Day1);
        await s.AddTruckAsync(Day2);
        Slot day1 = await s.AddSlotAsync(Day1), day2 = await s.AddSlotAsync(Day2);
        Delivery a = await s.AddDeliveryAsync(), b = await s.AddDeliveryAsync();
        await s.BookAsync(a, day1);
        await s.BookAsync(b, day2);

        for (var round = 0; round < 10; round++)
        {
            var (aTarget, bTarget) = round % 2 == 0 ? (day2, day1) : (day1, day2);
            var results = await Task.WhenAll(Task.Run(() => s.TryBookAsync(a, aTarget)), Task.Run(() => s.TryBookAsync(b, bTarget)));
            Assert.All(results, Assert.Null);
        }
    }

    [Fact]
    public async Task After_the_cutoff_a_booked_delivery_cannot_move_but_a_failed_one_can_be_rebooked()
    {
        var s = await CreateAsync(sql);
        var truck = await s.AddTruckAsync(Day1);
        await s.AddTruckAsync(Day2);
        Slot day1 = await s.AddSlotAsync(Day1), day2 = await s.AddSlotAsync(Day2);
        Delivery booked = await s.AddDeliveryAsync(), failed = await s.AddDeliveryAsync();
        await s.BookAsync(booked, day1);
        await s.BookAsync(failed, day1);

        // Wednesday 18:00 Swedish time: Day1 (Thursday) is closed for changes, Day2 (Friday) is still open.
        var afterCutoff = new DateTimeOffset(2026, 11, 11, 17, 0, 0, TimeSpan.Zero);
        Assert.Equal(BookingErrors.CutoffPassed, await s.TryBookAsync(booked, day2, afterCutoff));
        Assert.Equal(day1.Id, (await s.ReloadAsync(booked)).SlotId);

        // The other delivery goes out on Thursday and fails at the door.
        var route = new Route(truck.Id, Day1);
        await s.SaveAsync(route);
        await using (var db = s.Db())
        {
            var d = await db.Deliveries.SingleAsync(x => x.Id == failed.Id);
            var dispatcher = new Actor(ActorType.Dispatcher, "user-1");
            d.AssignToRoute(route.Id, 1, dispatcher, Now);
            d.ChangeStatus(DeliveryStatus.Loaded, dispatcher, null, Now);
            d.ChangeStatus(DeliveryStatus.OutForDelivery, dispatcher, null, Now);
            d.ChangeStatus(DeliveryStatus.Failed, dispatcher, "Nobody home", Now);
            await db.SaveChangesAsync();
        }
        Assert.Equal(1, (await s.ZoneDayAsync(Day1)).ReservedM3); // the failed one no longer takes up room

        // Thursday afternoon: its own slot is long past the cutoff, and it can still be rebooked.
        var thursdayAfternoon = new DateTimeOffset(2026, 11, 12, 14, 0, 0, TimeSpan.Zero);
        Assert.Null(await s.TryBookAsync(failed, day2, thursdayAfternoon));
        var rebooked = await s.ReloadAsync(failed);
        Assert.Equal((DeliveryStatus.Booked, day2.Id, null), (rebooked.Status, rebooked.SlotId!.Value, rebooked.FailureReason));
    }

    [Fact]
    public async Task Only_slots_in_the_deliverys_own_zone_can_be_booked()
    {
        var s = await CreateAsync(sql);
        await s.AddTruckAsync(Day1);
        await s.AddTruckAsync(Day1, zone: s.South);
        Slot citySlot = await s.AddSlotAsync(Day1), southSlot = await s.AddSlotAsync(Day1, zone: s.South);
        Delivery inCity = await s.AddDeliveryAsync(postcode: "114 55"),
            inSouth = await s.AddDeliveryAsync(postcode: "126 49"),
            nowhere = await s.AddDeliveryAsync(postcode: "411 01");

        Assert.Equal(BookingErrors.WrongZone, await s.TryBookAsync(inCity, southSlot));
        Assert.Equal(BookingErrors.WrongZone, await s.TryBookAsync(nowhere, citySlot));
        Assert.Null(await s.TryBookAsync(inSouth, southSlot));
        Assert.Equal([citySlot.Id], (await s.AvailableAsync(inCity)).Select(a => a.Id));
        Assert.Empty(await s.AvailableAsync(nowhere)); // outside every zone: an empty list, not an error
    }

    [Fact]
    public async Task The_slot_list_only_shows_what_can_actually_be_booked()
    {
        var s = await CreateAsync(sql);
        await s.AddTruckAsync(Day1);
        await s.AddTruckAsync(Day2);
        var open = await s.AddSlotAsync(Day1, 8);
        var full = await s.AddSlotAsync(Day1, 12, max: 1);
        var closed = await s.AddSlotAsync(Day1, 16);
        var tomorrow = await s.AddSlotAsync(Day2, 8);
        await s.BookAsync(await s.AddDeliveryAsync(), full);
        await using (var db = s.Db())
            await new SlotService(db, Options).UpdateAsync(closed.Id, new UpdateSlotRequest(8, IsClosed: true));
        var delivery = await s.AddDeliveryAsync();

        Assert.Equal([open.Id, tomorrow.Id], (await s.AvailableAsync(delivery)).Select(a => a.Id));
        Assert.Equal(BookingErrors.SlotFull, await s.TryBookAsync(delivery, full));
        Assert.Equal(BookingErrors.SlotClosed, await s.TryBookAsync(delivery, closed));

        // Wednesday 18:00 Swedish time: Thursday is gone from the list, Friday is still there.
        var afterCutoff = new DateTimeOffset(2026, 11, 11, 17, 0, 0, TimeSpan.Zero);
        Assert.Equal([tomorrow.Id], (await s.AvailableAsync(delivery, afterCutoff)).Select(a => a.Id));
        Assert.Equal(BookingErrors.CutoffPassed, await s.TryBookAsync(delivery, open, afterCutoff));
    }

    [Fact]
    public async Task Taking_a_truck_off_a_day_is_allowed_and_shows_the_day_as_oversold()
    {
        var s = await CreateAsync(sql);
        var small = await s.AddTruckAsync(Day1, m3: 10m);
        var big = await s.AddTruckAsync(Day1, m3: 20m);
        var slot = await s.AddSlotAsync(Day1);
        await s.BookAsync(await s.AddDeliveryAsync(m3: 20m), slot);
        Assert.False((await s.ZoneDayAsync(Day1)).IsOversold); // 20 of 25.5 m3

        await using (var db = s.Db())
            await new SlotService(db, Options).SetZoneDayTrucksAsync(s.City.Id, Day1, new SetZoneDayTrucksRequest([small.Id]));

        var day = await s.ZoneDayAsync(Day1);
        Assert.Equal((8.5m, 20m, true), (day.SellableM3, day.ReservedM3, day.IsOversold));
        Assert.Equal([small.Id], day.TruckIds);
        Assert.Equal(BookingErrors.ZoneDayFull, await s.TryBookAsync(await s.AddDeliveryAsync(m3: 0.1m), slot));
        _ = big;
    }
}
