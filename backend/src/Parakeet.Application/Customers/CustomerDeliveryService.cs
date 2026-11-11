using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Parakeet.Application.Booking;
using Parakeet.Domain.Common;
using Parakeet.Domain.Deliveries;
using Parakeet.Domain.Orders;
using Parakeet.Domain.Slots;

namespace Parakeet.Application.Customers;

public record CustomerLineDto(string Description, int Quantity);
public record CustomerDeliveryDto(long OrderNumber, DeliveryStatus Status, DeliveryType DeliveryType, string RecipientName,
    string Street, string Postcode, string City, IReadOnlyList<CustomerLineDto> Lines, AvailableSlot? Slot,
    bool CanChangeSlot, DateOnly LinkValidThrough);
public record BookSlotRequest(Guid SlotId);

/// <summary>Everything the customer portal can do. Each call starts by checking that the link is still good.</summary>
public class CustomerDeliveryService(IAppDbContext db, BookingService booking, TimeProvider time, IOptions<BookingOptions> options)
{
    public const int DefaultSlotRangeDays = 21;
    public const int MaxSlotRangeDays = 62;

    public async Task<CustomerDeliveryDto> GetAsync(CustomerAccess access, CancellationToken ct = default)
    {
        var (delivery, slot, validThrough) = await AuthorizeAsync(access, ct);
        var order = await db.Orders.AsNoTracking().Include(o => o.Lines).SingleAsync(o => o.Id == delivery.OrderId, ct);

        var rules = options.Value.ToRules();
        var canChange = DeliveryStatusRules.CanBook(delivery.Status)
            && (slot is null
                || !DeliveryStatusRules.RebookNeedsCurrentSlotBeforeCutoff(delivery.Status)
                || SlotAvailability.IsBeforeCutoff(slot.Date, StockholmTime.ToLocal(time.GetUtcNow()), rules));

        var postcode = order.Address.Postcode.ToString("000 00");
        return new CustomerDeliveryDto(order.OrderNumber, delivery.Status, order.DeliveryType, order.Recipient.Name,
            order.Address.Street, postcode, order.Address.City,
            order.Lines.OrderBy(l => l.Description).Select(l => new CustomerLineDto(l.Description, l.Quantity)).ToList(),
            slot is null ? null : new AvailableSlot(slot.Id, slot.Date, slot.Start, slot.End),
            canChange, validThrough);
    }

    public async Task<IReadOnlyList<AvailableSlot>> GetSlotsAsync(CustomerAccess access, DateOnly? from, DateOnly? to,
        CancellationToken ct = default)
    {
        await AuthorizeAsync(access, ct);
        var start = from ?? StockholmTime.Today(time.GetUtcNow());
        var end = to ?? start.AddDays(DefaultSlotRangeDays);
        if (end < start || end.DayNumber - start.DayNumber > MaxSlotRangeDays)
            throw new DomainException("InvalidRange", $"Ask for at most {MaxSlotRangeDays} days, with 'from' before 'to'.");
        return await booking.GetAvailableSlotsAsync(access.DeliveryId, start, end, ct);
    }

    public async Task<CustomerDeliveryDto> BookAsync(CustomerAccess access, Guid slotId, CancellationToken ct = default)
    {
        await AuthorizeAsync(access, ct);
        await booking.BookAsync(access.DeliveryId, slotId, Actor.Customer, ct);
        return await GetAsync(access, ct);
    }

    /// <summary>
    /// The token's signature was already checked by the API. Here we check the two things only the
    /// database knows: the link hasn't been replaced, and it isn't too long after the delivery date.
    /// </summary>
    private async Task<(Delivery Delivery, Slot? Slot, DateOnly ValidThrough)> AuthorizeAsync(CustomerAccess access, CancellationToken ct)
    {
        var delivery = await db.Deliveries.AsNoTracking().SingleOrDefaultAsync(d => d.Id == access.DeliveryId, ct)
            ?? throw new UnauthorizedException("LinkInvalid", "This link doesn't work.");
        if (delivery.LinkVersion != access.LinkVersion)
            throw new UnauthorizedException("LinkRevoked", "This link has been replaced by a newer one.");

        var slot = delivery.SlotId is { } slotId
            ? await db.Slots.AsNoTracking().SingleAsync(s => s.Id == slotId, ct)
            : null;
        // No slot yet: count from the day the order came in, so an ignored link doesn't live forever.
        var countFrom = slot?.Date ?? StockholmTime.Today(
            await db.Orders.Where(o => o.Id == delivery.OrderId).Select(o => o.CreatedAt).SingleAsync(ct));
        var validThrough = countFrom.AddDays(options.Value.LinkValidityDaysAfterDelivery);
        if (StockholmTime.Today(time.GetUtcNow()) > validThrough)
            throw new UnauthorizedException("LinkExpired", "This link has expired.");

        return (delivery, slot, validThrough);
    }
}
