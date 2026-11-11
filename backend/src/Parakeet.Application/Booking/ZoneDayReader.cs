using Microsoft.EntityFrameworkCore;
using Parakeet.Domain.Deliveries;
using Parakeet.Domain.Slots;

namespace Parakeet.Application.Booking;

/// <summary>Truck capacity and what is already reserved, per zone and date. Booked counts per slot.</summary>
internal class ZoneDaySnapshot
{
    public Dictionary<(Guid ZoneId, DateOnly Date), (decimal M3, decimal Kg)> Capacity { get; } = [];
    public Dictionary<(Guid ZoneId, DateOnly Date), (decimal M3, decimal Kg)> Reserved { get; } = [];
    public Dictionary<Guid, int> BookedPerSlot { get; } = [];

    public ZoneDayLoad Load(Guid zoneId, DateOnly date)
    {
        var capacity = Capacity.GetValueOrDefault((zoneId, date));
        var reserved = Reserved.GetValueOrDefault((zoneId, date));
        return new ZoneDayLoad(capacity.M3, capacity.Kg, reserved.M3, reserved.Kg);
    }

    public int Booked(Guid slotId) => BookedPerSlot.GetValueOrDefault(slotId);
}

internal static class ZoneDayReader
{
    /// <param name="zoneId">Null reads every zone.</param>
    /// <param name="excludeDeliveryId">Left out of the sums: a delivery being moved must not compete with itself.</param>
    public static async Task<ZoneDaySnapshot> LoadAsync(IAppDbContext db, Guid? zoneId, DateOnly from, DateOnly to,
        Guid? excludeDeliveryId, CancellationToken ct)
    {
        var snapshot = new ZoneDaySnapshot();

        var capacity = await (
            from zdt in db.ZoneDayTrucks
            join truck in db.Trucks on zdt.TruckId equals truck.Id
            where zdt.Date >= @from && zdt.Date <= to && truck.IsActive && (zoneId == null || zdt.ZoneId == zoneId)
            group truck by new { zdt.ZoneId, zdt.Date } into g
            select new { g.Key.ZoneId, g.Key.Date, M3 = g.Sum(t => t.CapacityM3), Kg = g.Sum(t => t.CapacityKg) }
        ).ToListAsync(ct);
        foreach (var c in capacity)
            snapshot.Capacity[(c.ZoneId, c.Date)] = (c.M3, c.Kg);

        // A failed delivery keeps its old slot for history but no longer takes up room.
        var perSlot = await (
            from delivery in db.Deliveries
            join slot in db.Slots on delivery.SlotId equals (Guid?)slot.Id
            where slot.Date >= @from && slot.Date <= to
                && delivery.Status != DeliveryStatus.Failed
                && (excludeDeliveryId == null || delivery.Id != excludeDeliveryId)
                && (zoneId == null || slot.ZoneId == zoneId)
            group delivery by new { slot.Id, slot.ZoneId, slot.Date } into g
            select new { g.Key.Id, g.Key.ZoneId, g.Key.Date, Count = g.Count(), M3 = g.Sum(d => d.VolumeM3), Kg = g.Sum(d => d.WeightKg) }
        ).ToListAsync(ct);
        foreach (var s in perSlot)
        {
            snapshot.BookedPerSlot[s.Id] = s.Count;
            var soFar = snapshot.Reserved.GetValueOrDefault((s.ZoneId, s.Date));
            snapshot.Reserved[(s.ZoneId, s.Date)] = (soFar.M3 + s.M3, soFar.Kg + s.Kg);
        }

        return snapshot;
    }
}
