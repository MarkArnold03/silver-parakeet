using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Parakeet.Domain.Common;
using Parakeet.Domain.Deliveries;
using Parakeet.Domain.Slots;
using Parakeet.Domain.Zones;

namespace Parakeet.Application.Booking;

public record AvailableSlot(Guid Id, DateOnly Date, TimeOnly Start, TimeOnly End);

public class BookingService(IAppDbContext db, TimeProvider time, IOptions<BookingOptions> options)
{
    private readonly BookingRules _rules = options.Value.ToRules();

    /// <summary>The slots this delivery could book right now. An empty list is a normal answer.</summary>
    public async Task<IReadOnlyList<AvailableSlot>> GetAvailableSlotsAsync(Guid deliveryId, DateOnly from, DateOnly to,
        CancellationToken ct = default)
    {
        var delivery = await db.Deliveries.AsNoTracking().SingleOrDefaultAsync(d => d.Id == deliveryId, ct)
            ?? throw new NotFoundException("DeliveryNotFound", "The delivery doesn't exist.");
        if (await FindZoneIdAsync(delivery, ct) is not { } zoneId)
            return [];

        var slots = await db.Slots.AsNoTracking()
            .Where(s => s.ZoneId == zoneId && s.Date >= from && s.Date <= to)
            .OrderBy(s => s.Date).ThenBy(s => s.Start)
            .ToListAsync(ct);
        var snapshot = await ZoneDayReader.LoadAsync(db, zoneId, from, to, delivery.Id, ct);
        var localNow = StockholmTime.ToLocal(time.GetUtcNow());

        return slots
            .Where(s => SlotAvailability.WhyNotBookable(s, zoneId, snapshot.Booked(s.Id), snapshot.Load(zoneId, s.Date),
                delivery.VolumeM3, delivery.WeightKg, localNow, _rules) is null)
            .Select(s => new AvailableSlot(s.Id, s.Date, s.Start, s.End))
            .ToList();
    }

    /// <summary>Book a slot or move to another one. Throws ConflictException with a BookingErrors code if it can't.</summary>
    public async Task BookAsync(Guid deliveryId, Guid slotId, Actor actor, CancellationToken ct = default)
    {
        var delivery = await db.Deliveries.SingleOrDefaultAsync(d => d.Id == deliveryId, ct)
            ?? throw new NotFoundException("DeliveryNotFound", "The delivery doesn't exist.");
        var target = await db.Slots.AsNoTracking().SingleOrDefaultAsync(s => s.Id == slotId, ct)
            ?? throw new NotFoundException("SlotNotFound", "The slot doesn't exist.");
        var current = delivery.SlotId is { } currentId
            ? await db.Slots.AsNoTracking().SingleAsync(s => s.Id == currentId, ct)
            : null;

        if (!DeliveryStatusRules.CanBook(delivery.Status))
            throw new ConflictException("RebookNotAllowed", $"A delivery that is {delivery.Status} can't be booked or rebooked.");
        var moving = current is not null && DeliveryStatusRules.RebookNeedsCurrentSlotBeforeCutoff(delivery.Status);
        if (moving && current!.Id == target.Id)
            return;

        // Lock the day we book into. When moving, also the day we leave, because its free capacity changes too.
        // The keys are always taken in the same (sorted) order, so two moves in opposite directions can't deadlock.
        var keys = new List<string> { LockKeys.ZoneDay(target.ZoneId, target.Date) };
        if (moving)
            keys.Add(LockKeys.ZoneDay(current!.ZoneId, current.Date));
        await using var transaction = await db.BeginLockedTransactionAsync(keys, ct);

        // Everything from here is read while we hold the lock, so nobody can change it under us.
        target = await db.Slots.AsNoTracking().SingleAsync(s => s.Id == slotId, ct);
        var now = time.GetUtcNow();
        var localNow = StockholmTime.ToLocal(now);

        if (moving && !SlotAvailability.IsBeforeCutoff(current!.Date, localNow, _rules))
            throw new ConflictException(BookingErrors.CutoffPassed, "It is too late to move this delivery.");

        var zoneId = await FindZoneIdAsync(delivery, ct);
        var snapshot = await ZoneDayReader.LoadAsync(db, target.ZoneId, target.Date, target.Date, delivery.Id, ct);
        var refusal = SlotAvailability.WhyNotBookable(target, zoneId, snapshot.Booked(target.Id),
            snapshot.Load(target.ZoneId, target.Date), delivery.VolumeM3, delivery.WeightKg, localNow, _rules);
        if (refusal is not null)
            throw new ConflictException(refusal, "That time is no longer available.");

        delivery.Book(target.Id, actor, now);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private async Task<Guid?> FindZoneIdAsync(Delivery delivery, CancellationToken ct)
    {
        var postcode = await db.Orders.Where(o => o.Id == delivery.OrderId).Select(o => o.Address.Postcode).SingleAsync(ct);
        // ponytail: loads every zone. There are a handful. Query the ranges table if that ever changes.
        var zones = await db.Zones.AsNoTracking().ToListAsync(ct);
        return ZoneRules.FindZone(zones, postcode)?.Id;
    }
}
