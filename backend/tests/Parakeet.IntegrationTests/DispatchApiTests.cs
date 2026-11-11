using System.Net;
using System.Net.Http.Json;
using Parakeet.Application.Slots;
using Parakeet.Application.Trucks;
using Parakeet.Application.Zones;

namespace Parakeet.IntegrationTests;

public class DispatchApiTests(SqlServerFixture sql) : ApiTestBase(sql)
{
    private static readonly DateOnly Monday = new(2026, 11, 16);

    private async Task<TruckDto> CreateTruckAsync(string name, string plate, decimal m3 = 20m, decimal kg = 1000m)
        => await ReadAsync<TruckDto>(await Client.PostAsJsonAsync("/api/trucks", new TruckRequest(name, plate, m3, kg)));

    private async Task<ZoneDto> CreateZoneAsync(string name, int from, int to)
        => await ReadAsync<ZoneDto>(await Client.PostAsJsonAsync("/api/zones", new ZoneRequest(name, [new(from, to)])));

    [Fact]
    public async Task Trucks_can_be_created_changed_and_deactivated()
    {
        await AuthenticateAsync();

        var created = await Client.PostAsJsonAsync("/api/trucks", new TruckRequest(" Bil 1 ", "ABC123", 18.5m, 1200m));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var truck = await ReadAsync<TruckDto>(created);
        Assert.Equal(("Bil 1", 18.5m, true), (truck.Name, truck.CapacityM3, truck.IsActive));
        Assert.Equal($"/api/trucks/{truck.Id}", created.Headers.Location!.ToString());

        var updated = await ReadAsync<TruckDto>(await Client.PutAsJsonAsync($"/api/trucks/{truck.Id}",
            new TruckRequest("Bil 1", "ABC123", 20m, 1500m, IsActive: false)));
        Assert.Equal((20m, 1500m, false), (updated.CapacityM3, updated.CapacityKg, updated.IsActive));

        Assert.Equal([updated], await ReadAsync<TruckDto[]>(await Client.GetAsync("/api/trucks")));
        Assert.Equal(updated, await ReadAsync<TruckDto>(await Client.GetAsync($"/api/trucks/{truck.Id}")));
    }

    [Fact]
    public async Task Bad_truck_input_is_refused_with_a_clear_status()
    {
        await AuthenticateAsync();
        await CreateTruckAsync("Bil 1", "ABC123");

        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync("/api/trucks", new TruckRequest("Bil 2", "DEF456", 0m, 1000m))).StatusCode);
        Assert.Equal("Required", await ErrorCodeAsync(await Client.PostAsJsonAsync("/api/trucks", new TruckRequest(" ", "DEF456", 10m, 1000m)), 400));
        Assert.Equal("Duplicate", await ErrorCodeAsync(await Client.PostAsJsonAsync("/api/trucks", new TruckRequest("Bil 2", "ABC123", 10m, 1000m)), 409));
        Assert.Equal("TruckNotFound", await ErrorCodeAsync(await Client.GetAsync($"/api/trucks/{Guid.NewGuid()}"), 404));
    }

    [Fact]
    public async Task Zones_can_be_managed_but_postcodes_cannot_belong_to_two_zones()
    {
        await AuthenticateAsync();
        var city = await CreateZoneAsync("City", 11100, 11899);
        var south = await CreateZoneAsync("South", 12000, 12999);

        Assert.Equal("PostcodeOverlap", await ErrorCodeAsync(
            await Client.PostAsJsonAsync("/api/zones", new ZoneRequest("West", [new(11800, 11950)])), 409));
        Assert.Equal("PostcodeOverlap", await ErrorCodeAsync(
            await Client.PutAsJsonAsync($"/api/zones/{south.Id}", new ZoneRequest("South", [new(11899, 12999)])), 409));
        Assert.Equal("InvalidPostcodeRange", await ErrorCodeAsync(
            await Client.PostAsJsonAsync("/api/zones", new ZoneRequest("West", [new(13000, 12000)])), 400));

        var changed = await ReadAsync<ZoneDto>(await Client.PutAsJsonAsync($"/api/zones/{south.Id}",
            new ZoneRequest("Söder", [new(14100, 14199), new(12000, 12499)])));
        Assert.Equal("Söder", changed.Name);
        Assert.Equal([new(12000, 12499), new(14100, 14199)], changed.PostcodeRanges);

        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync($"/api/zones/{city.Id}")).StatusCode);
        Assert.Equal(["Söder"], (await ReadAsync<ZoneDto[]>(await Client.GetAsync("/api/zones"))).Select(z => z.Name));
    }

    [Fact]
    public async Task Slots_are_generated_once_and_can_be_closed_or_deleted()
    {
        await AuthenticateAsync();
        var zone = await CreateZoneAsync("City", 11100, 11899);
        var request = new GenerateSlotsRequest(zone.Id, Monday, Monday.AddDays(6),
            [DayOfWeek.Monday, DayOfWeek.Wednesday], [new(new(8, 0), new(12, 0)), new(new(12, 0), new(16, 0))], 8);

        Assert.Equal(new GenerateSlotsResult(4, 0), await ReadAsync<GenerateSlotsResult>(await Client.PostAsJsonAsync("/api/slots/generate", request)));
        // Running it again, with one more day, only adds what is missing.
        Assert.Equal(new GenerateSlotsResult(2, 4), await ReadAsync<GenerateSlotsResult>(await Client.PostAsJsonAsync("/api/slots/generate",
            request with { Weekdays = [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday] })));

        var slots = await ReadAsync<SlotDto[]>(await Client.GetAsync($"/api/slots?zoneId={zone.Id}&from={Monday:yyyy-MM-dd}&to={Monday.AddDays(6):yyyy-MM-dd}"));
        Assert.Equal(6, slots.Length);
        Assert.Equal((Monday, new TimeOnly(8, 0), new TimeOnly(12, 0), 8, false, 0),
            (slots[0].Date, slots[0].Start, slots[0].End, slots[0].MaxDeliveries, slots[0].IsClosed, slots[0].BookedCount));

        var closed = await ReadAsync<SlotDto>(await Client.PutAsJsonAsync($"/api/slots/{slots[0].Id}", new UpdateSlotRequest(4, true)));
        Assert.Equal((4, true), (closed.MaxDeliveries, closed.IsClosed));

        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync($"/api/slots/{slots[1].Id}")).StatusCode);
        Assert.Equal("SlotNotFound", await ErrorCodeAsync(await Client.DeleteAsync($"/api/slots/{slots[1].Id}"), 404));

        // A zone that still has slots can't be deleted.
        Assert.Equal("InUse", await ErrorCodeAsync(await Client.DeleteAsync($"/api/zones/{zone.Id}"), 409));
        Assert.Equal("InvalidDateRange", await ErrorCodeAsync(await Client.PostAsJsonAsync("/api/slots/generate",
            request with { To = Monday.AddDays(400) }), 400));
    }

    [Fact]
    public async Task Zone_day_trucks_decide_what_can_be_sold_and_a_truck_serves_one_zone_per_day()
    {
        await AuthenticateAsync();
        var city = await CreateZoneAsync("City", 11100, 11899);
        var south = await CreateZoneAsync("South", 12000, 12999);
        var truck1 = await CreateTruckAsync("Bil 1", "ABC123", 20m, 1000m);
        var truck2 = await CreateTruckAsync("Bil 2", "DEF456", 30m, 2000m);
        var url = $"/api/zone-days/{city.Id}/{Monday:yyyy-MM-dd}/trucks";

        var day = await ReadAsync<ZoneDayDto>(await Client.PutAsJsonAsync(url, new SetZoneDayTrucksRequest([truck1.Id, truck2.Id])));
        Assert.Equal((50m, 3000m, 42.5m, 3000m, 0m, false),
            (day.TruckM3, day.TruckKg, day.SellableM3, day.SellableKg, day.ReservedM3, day.IsOversold));

        Assert.Equal("TruckBusy", await ErrorCodeAsync(await Client.PutAsJsonAsync(
            $"/api/zone-days/{south.Id}/{Monday:yyyy-MM-dd}/trucks", new SetZoneDayTrucksRequest([truck2.Id])), 409));

        // Take truck 2 off City, and now South can have it.
        day = await ReadAsync<ZoneDayDto>(await Client.PutAsJsonAsync(url, new SetZoneDayTrucksRequest([truck1.Id])));
        Assert.Equal(17m, day.SellableM3);
        await ReadAsync<ZoneDayDto>(await Client.PutAsJsonAsync(
            $"/api/zone-days/{south.Id}/{Monday:yyyy-MM-dd}/trucks", new SetZoneDayTrucksRequest([truck2.Id])));

        var days = await ReadAsync<ZoneDayDto[]>(await Client.GetAsync($"/api/zone-days?from={Monday:yyyy-MM-dd}&to={Monday:yyyy-MM-dd}"));
        Assert.Equal(2, days.Length);
        Assert.Equal([truck2.Id], days.Single(d => d.ZoneId == south.Id).TruckIds);

        Assert.Equal("TruckNotFound", await ErrorCodeAsync(await Client.PutAsJsonAsync(url, new SetZoneDayTrucksRequest([Guid.NewGuid()])), 404));
    }
}
