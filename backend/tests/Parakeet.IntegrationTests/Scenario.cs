using Microsoft.Extensions.Options;
using Parakeet.Application.Booking;
using Parakeet.Application.Slots;
using Parakeet.Domain.Deliveries;
using Parakeet.Domain.Merchants;
using Parakeet.Domain.Orders;
using Parakeet.Domain.Slots;
using Parakeet.Domain.Trucks;
using Parakeet.Domain.Zones;
using Parakeet.Infrastructure.Persistence;

namespace Parakeet.IntegrationTests;

public class FixedTime(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>
/// A small world in its own database: zone "City" (11100-11899) with slots on two days, built by each test.
/// Every call that touches the database opens its own connection, like separate web requests do.
/// </summary>
public class Scenario
{
    // Tuesday 10 Nov 2026, 10:00 Swedish time. Day1 is Thursday, so its cutoff is Wednesday 18:00.
    public static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
    public static readonly DateOnly Day1 = new(2026, 11, 12);
    public static readonly DateOnly Day2 = new(2026, 11, 13);

    private readonly string _connectionString;
    private int _orderCounter;

    public Zone City { get; } = new("City", [new PostcodeRange(11100, 11899)]);
    public Zone South { get; } = new("South", [new PostcodeRange(12000, 12999)]);
    public Merchant Merchant { get; } = new("Test AB", Guid.NewGuid().ToString("N"), "pk_test");

    private Scenario(string connectionString) => _connectionString = connectionString;

    public static async Task<Scenario> CreateAsync(SqlServerFixture sql)
    {
        var scenario = new Scenario(await sql.MigratedDatabaseAsync());
        await scenario.SaveAsync(scenario.City, scenario.South, scenario.Merchant);
        return scenario;
    }

    public AppDbContext Db() => SqlServerFixture.Context(_connectionString);

    public async Task SaveAsync(params object[] entities)
    {
        await using var db = Db();
        db.AddRange(entities);
        await db.SaveChangesAsync();
    }

    public async Task<Truck> AddTruckAsync(DateOnly date, decimal m3 = 20m, decimal kg = 1000m, Zone? zone = null)
    {
        var truck = new Truck($"Truck {Guid.NewGuid():N}"[..12], Guid.NewGuid().ToString("N")[..8], m3, kg);
        await SaveAsync(truck, new ZoneDayTruck((zone ?? City).Id, date, truck.Id));
        return truck;
    }

    public async Task<Slot> AddSlotAsync(DateOnly date, int startHour = 8, int max = 8, Zone? zone = null)
    {
        var slot = new Slot((zone ?? City).Id, date, new TimeOnly(startHour, 0), new TimeOnly(startHour + 4, 0), max);
        await SaveAsync(slot);
        return slot;
    }

    public async Task<Delivery> AddDeliveryAsync(decimal m3 = 1m, decimal kg = 50m, string postcode = "114 55")
    {
        var order = new Order(Merchant.Id, $"REF-{Interlocked.Increment(ref _orderCounter)}",
            new Recipient("Anna Andersson", "+46701234567", null), new Address("Storgatan 1", postcode, "Stockholm"),
            DeliveryType.Curbside, [new OrderLine("A-1", "Sak", 1, m3, kg)], new string('a', 64), Now);
        var delivery = new Delivery(order);
        await SaveAsync(order, delivery);
        return delivery;
    }

    public static IOptions<BookingOptions> Options => Microsoft.Extensions.Options.Options.Create(new BookingOptions());

    public async Task BookAsync(Delivery delivery, Slot slot, DateTimeOffset? now = null)
    {
        await using var db = Db();
        await new BookingService(db, new FixedTime(now ?? Now), Options).BookAsync(delivery.Id, slot.Id, Actor.Customer);
    }

    /// <summary>Null when the booking went through, otherwise the error code.</summary>
    public async Task<string?> TryBookAsync(Delivery delivery, Slot slot, DateTimeOffset? now = null)
    {
        try
        {
            await BookAsync(delivery, slot, now);
            return null;
        }
        catch (Domain.Common.DomainException e)
        {
            return e.Code;
        }
    }

    public async Task<IReadOnlyList<AvailableSlot>> AvailableAsync(Delivery delivery, DateTimeOffset? now = null)
    {
        await using var db = Db();
        return await new BookingService(db, new FixedTime(now ?? Now), Options)
            .GetAvailableSlotsAsync(delivery.Id, Day1, Day2);
    }

    public async Task<ZoneDayDto> ZoneDayAsync(DateOnly date)
    {
        await using var db = Db();
        return (await new SlotService(db, Options).ListZoneDaysAsync(City.Id, date, date)).Single();
    }

    public async Task<Delivery> ReloadAsync(Delivery delivery)
    {
        await using var db = Db();
        return await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(
            Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.Include(db.Deliveries, d => d.Events),
            d => d.Id == delivery.Id);
    }
}
