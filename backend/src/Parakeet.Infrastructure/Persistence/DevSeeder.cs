using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Parakeet.Domain.Deliveries;
using Parakeet.Domain.Merchants;
using Parakeet.Domain.Orders;
using Parakeet.Domain.Payments;
using Parakeet.Domain.Slots;
using Parakeet.Domain.Trucks;
using Parakeet.Domain.Zones;

namespace Parakeet.Infrastructure.Persistence;

/// <summary>
/// Sample data for local development. Program.cs only calls this when the environment is Development.
/// Does nothing if the database already has zones, so it is safe to run on every start.
/// </summary>
public static class DevSeeder
{
    /// <summary>Not a secret: it only exists in databases created by this seeder.</summary>
    public const string DevMerchantApiKey = "pk_dev_parakeet_local_only";

    public static async Task SeedAsync(AppDbContext db, TimeProvider time, CancellationToken ct = default)
    {
        if (await db.Zones.AnyAsync(ct))
            return;

        var now = time.GetUtcNow();
        var stockholm = TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, stockholm).DateTime);

        var city = new Zone("Stockholm City", [new PostcodeRange(11100, 11899)]);
        var south = new Zone("Stockholm Syd", [new PostcodeRange(12000, 12999), new PostcodeRange(14100, 14199)]);
        ZoneRules.EnsureNoOverlap([city, south]);
        db.Zones.AddRange(city, south);

        var truck1 = new Truck("Bil 1", "ABC123", 18m, 1200m);
        var truck2 = new Truck("Bil 2", "DEF456", 18m, 1200m);
        var truck3 = new Truck("Bil 3 (stor)", "GHI789", 35m, 3000m);
        db.Trucks.AddRange(truck1, truck2, truck3);

        (TimeOnly Start, TimeOnly End)[] windows = [(new(8, 0), new(12, 0)), (new(12, 0), new(16, 0)), (new(16, 0), new(20, 0))];
        for (var day = 1; day <= 14; day++)
        {
            var date = today.AddDays(day);
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;
            db.ZoneDayTrucks.AddRange(new ZoneDayTruck(city.Id, date, truck1.Id), new ZoneDayTruck(south.Id, date, truck2.Id));
            foreach (var zone in new[] { city, south })
                foreach (var (start, end) in windows)
                    db.Slots.Add(new Slot(zone.Id, date, start, end, maxDeliveries: 8));
        }

        db.ExtraServices.AddRange(
            new ExtraService("CarryIn", "Inbärning", "Carry-in", 29900),
            new ExtraService("Assembly", "Montering", "Assembly", 59900),
            new ExtraService("PackagingRemoval", "Bortforsling av emballage", "Packaging removal", 14900));

        var keyHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(DevMerchantApiKey)));
        var merchant = new Merchant("Demo Möbler AB", keyHash, DevMerchantApiKey[..10]);
        db.Merchants.Add(merchant);

        (string Ref, string Name, string Street, string Postcode, string City, OrderLine[] Lines)[] orders =
        [
            ("DEMO-1001", "Anna Andersson", "Sveavägen 44", "111 34", "Stockholm",
                [new("SOFA-3", "3-sits soffa", 1, 1.850m, 62.5m), new("TABLE-C", "Soffbord", 1, 0.240m, 18m)]),
            ("DEMO-1002", "Erik Eriksson", "Götgatan 78", "118 30", "Stockholm",
                [new("BED-160", "Säng 160 cm", 1, 1.200m, 55m), new("MATT-160", "Madrass 160 cm", 1, 0.900m, 32m)]),
            ("DEMO-1003", "Maria Nilsson", "Hägerstensvägen 120", "126 49", "Hägersten",
                [new("CHAIR-D", "Matstol", 6, 0.125m, 4.25m), new("TABLE-D", "Matbord", 1, 0.450m, 38m)]),
        ];
        foreach (var o in orders)
        {
            var order = new Order(merchant.Id, o.Ref, new Recipient(o.Name, "+46701234567", null),
                new Address(o.Street, o.Postcode, o.City), DeliveryType.Curbside, o.Lines,
                Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(o.Ref))), now);
            db.Orders.Add(order);
            db.Deliveries.Add(new Delivery(order));
        }

        await db.SaveChangesAsync(ct);
    }
}
