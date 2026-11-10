using Microsoft.EntityFrameworkCore;
using Parakeet.Domain.Deliveries;
using Parakeet.Domain.Merchants;
using Parakeet.Domain.Orders;
using Parakeet.Domain.Trucks;
using Parakeet.Domain.Zones;
using Parakeet.Infrastructure.Persistence;

namespace Parakeet.IntegrationTests;

[Collection(SqlServerCollection.Name)]
public class DatabaseTests(SqlServerFixture sql)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);

    private static Order NewOrder(Guid merchantId, string reference) => new(
        merchantId, reference,
        new Recipient("Anna Andersson", "+46701234567", "anna@example.com"),
        new Address("Sveavägen 44", "111 34", "Stockholm"),
        DeliveryType.Doorstep,
        [new OrderLine("SOFA", "Soffa", 1, 1.850m, 62.50m), new OrderLine("CHAIR", "Stol", 4, 0.125m, 4.25m)],
        new string('a', 64), Now);

    private static async Task<Merchant> AddMerchantAsync(AppDbContext db)
    {
        var merchant = new Merchant("Test AB", Guid.NewGuid().ToString("N"), "pk_test");
        db.Merchants.Add(merchant);
        await db.SaveChangesAsync();
        return merchant;
    }

    [Fact]
    public async Task Migrations_apply_to_an_empty_database_and_match_the_model()
    {
        await using var db = SqlServerFixture.Context(sql.NewDatabaseConnectionString());

        await db.Database.MigrateAsync();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        // Fails if someone changes an entity or its mapping and forgets to add a migration.
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Order_and_delivery_round_trip()
    {
        var cs = await sql.MigratedDatabaseAsync();
        Guid orderId, deliveryId;

        await using (var db = SqlServerFixture.Context(cs))
        {
            var merchant = await AddMerchantAsync(db);
            var order = NewOrder(merchant.Id, "REF-1");
            order.Address.SetLocation(59.3365, 18.0630);
            var delivery = new Delivery(order);
            db.AddRange(order, delivery);
            await db.SaveChangesAsync();
            (orderId, deliveryId) = (order.Id, delivery.Id);
        }

        await using (var db = SqlServerFixture.Context(cs))
        {
            var order = await db.Orders.Include(o => o.Lines).SingleAsync(o => o.Id == orderId);
            Assert.True(order.OrderNumber >= 100000); // assigned by the database sequence
            Assert.Equal("Anna Andersson", order.Recipient.Name);
            Assert.Equal(11134, order.Address.Postcode);
            Assert.Equal(GeocodeStatus.Ok, order.Address.GeocodeStatus);
            Assert.Equal(2, order.Lines.Count);
            Assert.Equal(2.350m, order.TotalVolumeM3);
            Assert.Equal(79.50m, order.TotalWeightKg);

            var delivery = await db.Deliveries.SingleAsync(d => d.Id == deliveryId);
            Assert.Equal(DeliveryStatus.AwaitingBooking, delivery.Status);
            Assert.Equal(2.350m, delivery.VolumeM3);
            Assert.Null(delivery.AlternativeRecipient);
            Assert.NotEmpty(delivery.RowVersion);

            // Stored as text, so the table is readable without the code next to it.
            var status = await db.Database.SqlQuery<string>($"SELECT Status AS Value FROM Deliveries").SingleAsync();
            Assert.Equal("AwaitingBooking", status);
        }
    }

    [Fact]
    public async Task Changes_on_a_loaded_delivery_are_saved_with_their_events()
    {
        var cs = await sql.MigratedDatabaseAsync();
        Guid deliveryId;
        await using (var db = SqlServerFixture.Context(cs))
        {
            var order = NewOrder((await AddMerchantAsync(db)).Id, "REF-1");
            var delivery = new Delivery(order);
            db.AddRange(order, delivery);
            await db.SaveChangesAsync();
            deliveryId = delivery.Id;
        }

        await using (var db = SqlServerFixture.Context(cs))
        {
            var zone = new Zone("City", [new PostcodeRange(11100, 11899)]);
            var slot = new Domain.Slots.Slot(zone.Id, new DateOnly(2026, 11, 12), new TimeOnly(8, 0), new TimeOnly(12, 0), 8);
            db.AddRange(zone, slot);
            var delivery = await db.Deliveries.SingleAsync(d => d.Id == deliveryId);
            delivery.Book(slot.Id, Actor.Customer, Now);
            delivery.SetAlternativeRecipient(new AlternativeRecipient("Granne Bengt", "+46709999999"));
            await db.SaveChangesAsync();
        }

        await using (var db = SqlServerFixture.Context(cs))
        {
            var delivery = await db.Deliveries.Include(d => d.Events).SingleAsync(d => d.Id == deliveryId);
            Assert.Equal(DeliveryStatus.Booked, delivery.Status);
            Assert.Equal("Granne Bengt", delivery.AlternativeRecipient!.Name);
            var e = Assert.Single(delivery.Events);
            Assert.Equal((DeliveryEventType.StatusChanged, DeliveryStatus.Booked, ActorType.Customer, Now),
                (e.Type, e.ToStatus!.Value, e.ActorType, e.At));
        }
    }

    [Fact]
    public async Task Same_merchant_reference_twice_is_refused_by_the_database()
    {
        await using var db = SqlServerFixture.Context(await sql.MigratedDatabaseAsync());
        var merchant = await AddMerchantAsync(db);
        var other = await AddMerchantAsync(db);
        db.Orders.Add(NewOrder(merchant.Id, "REF-1"));
        db.Orders.Add(NewOrder(other.Id, "REF-1")); // another merchant may use the same reference
        await db.SaveChangesAsync();

        db.Orders.Add(NewOrder(merchant.Id, "REF-1"));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Two_edits_of_the_same_delivery_cannot_overwrite_each_other()
    {
        var cs = await sql.MigratedDatabaseAsync();
        await using var first = SqlServerFixture.Context(cs);
        var order = NewOrder((await AddMerchantAsync(first)).Id, "REF-1");
        first.AddRange(order, new Delivery(order));
        await first.SaveChangesAsync();

        await using var second = SqlServerFixture.Context(cs);
        var mine = await first.Deliveries.SingleAsync();
        var theirs = await second.Deliveries.SingleAsync();

        theirs.RevokeLinks();
        await second.SaveChangesAsync();

        mine.RevokeLinks(); // based on a copy that is now out of date
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => first.SaveChangesAsync());
    }

    [Fact]
    public async Task A_truck_serves_one_zone_per_day_and_zone_ranges_round_trip()
    {
        var cs = await sql.MigratedDatabaseAsync();
        var date = new DateOnly(2026, 11, 12);
        Zone city = new("City", [new PostcodeRange(11100, 11899)]),
            south = new("South", [new PostcodeRange(12000, 12999), new PostcodeRange(14100, 14199)]);
        var truck = new Truck("Bil 1", "ABC123", 18m, 1200m);

        await using (var db = SqlServerFixture.Context(cs))
        {
            db.AddRange(city, south, truck, new ZoneDayTruck(city.Id, date, truck.Id));
            await db.SaveChangesAsync();

            db.Add(new ZoneDayTruck(south.Id, date, truck.Id));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        await using (var db = SqlServerFixture.Context(cs))
        {
            var zones = await db.Zones.ToListAsync();
            Assert.Equal("South", ZoneRules.FindZone(zones, 14150)!.Name);
            Assert.Null(ZoneRules.FindZone(zones, 13000));

            // Replacing the ranges deletes the old rows and inserts the new ones.
            var loaded = zones.Single(z => z.Name == "South");
            loaded.SetPostcodeRanges([new PostcodeRange(12000, 12499)]);
            await db.SaveChangesAsync();
        }

        await using (var db = SqlServerFixture.Context(cs))
            Assert.Equal(1, await db.Database.SqlQuery<int>(
                $"SELECT COUNT(*) AS Value FROM PostcodeRanges WHERE ZoneId = {south.Id}").SingleAsync());
    }

    [Fact]
    public async Task Dev_seeder_fills_an_empty_database_once()
    {
        await using var db = SqlServerFixture.Context(await sql.MigratedDatabaseAsync());

        await DevSeeder.SeedAsync(db, TimeProvider.System);
        await DevSeeder.SeedAsync(db, TimeProvider.System); // second run must not duplicate anything

        Assert.Equal(2, await db.Zones.CountAsync());
        Assert.Equal(3, await db.Trucks.CountAsync());
        Assert.Equal(3, await db.Deliveries.CountAsync());
        Assert.Equal(3, await db.ExtraServices.CountAsync());
        Assert.True(await db.Slots.AnyAsync());
        ZoneRules.EnsureNoOverlap(await db.Zones.ToListAsync());
    }
}
