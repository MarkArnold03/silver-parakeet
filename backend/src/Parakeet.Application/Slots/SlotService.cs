using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Parakeet.Application.Booking;
using Parakeet.Domain.Common;
using Parakeet.Domain.Slots;
using Parakeet.Domain.Trucks;

namespace Parakeet.Application.Slots;

public record SlotDto(Guid Id, Guid ZoneId, DateOnly Date, TimeOnly Start, TimeOnly End, int MaxDeliveries, bool IsClosed, int BookedCount);
public record TimeWindow(TimeOnly Start, TimeOnly End);
public record GenerateSlotsRequest(Guid ZoneId, DateOnly From, DateOnly To, DayOfWeek[]? Weekdays, TimeWindow[]? Windows, int MaxDeliveries);
public record GenerateSlotsResult(int Created, int Skipped);
public record UpdateSlotRequest(int MaxDeliveries, bool IsClosed);
public record SetZoneDayTrucksRequest(Guid[]? TruckIds);
public record ZoneDayDto(Guid ZoneId, DateOnly Date, Guid[] TruckIds, decimal TruckM3, decimal TruckKg,
    decimal SellableM3, decimal SellableKg, decimal ReservedM3, decimal ReservedKg, bool IsOversold);

/// <summary>Slots and zone-days: what can be booked, and with which trucks behind it.</summary>
public class SlotService(IAppDbContext db, IOptions<BookingOptions> options)
{
    public const int MaxRangeDays = 92;
    private readonly BookingRules _rules = options.Value.ToRules();

    public async Task<IReadOnlyList<SlotDto>> ListAsync(Guid zoneId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        CheckRange(from, to);
        var slots = await db.Slots.AsNoTracking()
            .Where(s => s.ZoneId == zoneId && s.Date >= from && s.Date <= to)
            .OrderBy(s => s.Date).ThenBy(s => s.Start)
            .ToListAsync(ct);
        var snapshot = await ZoneDayReader.LoadAsync(db, zoneId, from, to, null, ct);
        return slots.Select(s => ToDto(s, snapshot.Booked(s.Id))).ToList();
    }

    /// <summary>Creates a slot for every chosen weekday and time window in the range. Existing slots are left alone.</summary>
    public async Task<GenerateSlotsResult> GenerateAsync(GenerateSlotsRequest request, CancellationToken ct = default)
    {
        CheckRange(request.From, request.To);
        var weekdays = (request.Weekdays ?? []).ToHashSet();
        var windows = (request.Windows ?? []).DistinctBy(w => w.Start).ToList();
        if (weekdays.Count == 0 || windows.Count == 0)
            throw new DomainException("Required", "Pick at least one weekday and one time window.");
        if (!await db.Zones.AnyAsync(z => z.Id == request.ZoneId, ct))
            throw new NotFoundException("ZoneNotFound", "The zone doesn't exist.");

        var existing = (await db.Slots
                .Where(s => s.ZoneId == request.ZoneId && s.Date >= request.From && s.Date <= request.To)
                .Select(s => new { s.Date, s.Start })
                .ToListAsync(ct))
            .Select(s => (s.Date, s.Start)).ToHashSet();

        int created = 0, skipped = 0;
        for (var date = request.From; date <= request.To; date = date.AddDays(1))
        {
            if (!weekdays.Contains(date.DayOfWeek)) continue;
            foreach (var window in windows)
            {
                if (existing.Contains((date, window.Start))) { skipped++; continue; }
                db.Slots.Add(new Slot(request.ZoneId, date, window.Start, window.End, request.MaxDeliveries));
                created++;
            }
        }
        await db.SaveChangesAsync(ct);
        return new GenerateSlotsResult(created, skipped);
    }

    public async Task<SlotDto> UpdateAsync(Guid id, UpdateSlotRequest request, CancellationToken ct = default)
    {
        var key = await db.Slots.Where(s => s.Id == id).Select(s => new { s.ZoneId, s.Date }).SingleOrDefaultAsync(ct)
            ?? throw SlotNotFound();
        // Same lock as booking, so a booking can't slip in between its check and our change.
        await using var transaction = await db.BeginLockedTransactionAsync([LockKeys.ZoneDay(key.ZoneId, key.Date)], ct);

        var slot = await db.Slots.SingleAsync(s => s.Id == id, ct);
        slot.SetMaxDeliveries(request.MaxDeliveries);
        if (request.IsClosed) slot.Close(); else slot.Reopen();
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        var booked = (await ZoneDayReader.LoadAsync(db, slot.ZoneId, slot.Date, slot.Date, null, ct)).Booked(slot.Id);
        return ToDto(slot, booked);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var slot = await db.Slots.SingleOrDefaultAsync(s => s.Id == id, ct) ?? throw SlotNotFound();
        await using var transaction = await db.BeginLockedTransactionAsync([LockKeys.ZoneDay(slot.ZoneId, slot.Date)], ct);
        if (await db.Deliveries.AnyAsync(d => d.SlotId == id, ct))
            throw new ConflictException("SlotInUse", "The slot has deliveries. Close it instead of deleting it.");
        db.Slots.Remove(slot);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<ZoneDayDto>> ListZoneDaysAsync(Guid? zoneId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        CheckRange(from, to);
        var trucks = (await db.ZoneDayTrucks.AsNoTracking()
                .Where(z => z.Date >= from && z.Date <= to && (zoneId == null || z.ZoneId == zoneId))
                .ToListAsync(ct))
            .ToLookup(z => (z.ZoneId, z.Date), z => z.TruckId);
        var snapshot = await ZoneDayReader.LoadAsync(db, zoneId, from, to, null, ct);

        // A zone-day is worth showing when it has trucks or something is booked on it.
        return trucks.Select(g => g.Key).Union(snapshot.Reserved.Keys)
            .OrderBy(k => k.Date).ThenBy(k => k.ZoneId)
            .Select(k => ToDto(k.ZoneId, k.Date, trucks[k].ToArray(), snapshot.Load(k.ZoneId, k.Date)))
            .ToList();
    }

    /// <summary>
    /// Says which trucks serve a zone on a date. Taking a truck away is always allowed, even when it leaves
    /// the day oversold: a truck that broke down has to come off, and dispatch sees the warning.
    /// </summary>
    public async Task<ZoneDayDto> SetZoneDayTrucksAsync(Guid zoneId, DateOnly date, SetZoneDayTrucksRequest request, CancellationToken ct = default)
    {
        var truckIds = (request.TruckIds ?? []).Distinct().ToList();
        if (!await db.Zones.AnyAsync(z => z.Id == zoneId, ct))
            throw new NotFoundException("ZoneNotFound", "The zone doesn't exist.");

        await using var transaction = await db.BeginLockedTransactionAsync([LockKeys.ZoneDay(zoneId, date)], ct);

        var trucks = await db.Trucks.AsNoTracking().Where(t => truckIds.Contains(t.Id)).ToListAsync(ct);
        if (trucks.Count != truckIds.Count)
            throw new NotFoundException("TruckNotFound", "One of the trucks doesn't exist.");
        if (trucks.FirstOrDefault(t => !t.IsActive) is { } inactive)
            throw new ConflictException("TruckInactive", $"{inactive.Name} is not active.");

        var sameDate = await db.ZoneDayTrucks.Where(z => z.Date == date).ToListAsync(ct);
        if (sameDate.FirstOrDefault(z => z.ZoneId != zoneId && truckIds.Contains(z.TruckId)) is { } busy)
            throw new ConflictException("TruckBusy",
                $"{trucks.Single(t => t.Id == busy.TruckId).Name} already serves another zone on {date:yyyy-MM-dd}.");

        var current = sameDate.Where(z => z.ZoneId == zoneId).ToList();
        db.ZoneDayTrucks.RemoveRange(current.Where(z => !truckIds.Contains(z.TruckId)));
        db.ZoneDayTrucks.AddRange(truckIds.Except(current.Select(z => z.TruckId)).Select(id => new ZoneDayTruck(zoneId, date, id)));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        var snapshot = await ZoneDayReader.LoadAsync(db, zoneId, date, date, null, ct);
        return ToDto(zoneId, date, truckIds.ToArray(), snapshot.Load(zoneId, date));
    }

    private ZoneDayDto ToDto(Guid zoneId, DateOnly date, Guid[] truckIds, ZoneDayLoad load)
        => new(zoneId, date, truckIds, load.TruckM3, load.TruckKg, load.SellableM3(_rules), load.SellableKg,
            load.ReservedM3, load.ReservedKg, load.IsOversold(_rules));

    private static SlotDto ToDto(Slot s, int booked)
        => new(s.Id, s.ZoneId, s.Date, s.Start, s.End, s.MaxDeliveries, s.IsClosed, booked);

    private static NotFoundException SlotNotFound() => new("SlotNotFound", "The slot doesn't exist.");

    private static void CheckRange(DateOnly from, DateOnly to)
    {
        if (to < from || to.DayNumber - from.DayNumber >= MaxRangeDays)
            throw new DomainException("InvalidDateRange", $"Pick a range of 1 to {MaxRangeDays} days.");
    }
}
