using Parakeet.Domain.Common;

namespace Parakeet.Domain.Slots;

/// <summary>A bookable time window in one zone on one date. Date and times are local Europe/Stockholm.</summary>
public class Slot
{
    private Slot() { }

    public Slot(Guid zoneId, DateOnly date, TimeOnly start, TimeOnly end, int maxDeliveries)
    {
        if (end <= start)
            throw new DomainException("InvalidSlot", "A slot must end after it starts.");
        Id = Guid.CreateVersion7();
        ZoneId = zoneId;
        Date = date;
        Start = start;
        End = end;
        SetMaxDeliveries(maxDeliveries);
    }

    public Guid Id { get; private set; }
    public Guid ZoneId { get; private set; }
    public DateOnly Date { get; private set; }
    public TimeOnly Start { get; private set; }
    public TimeOnly End { get; private set; }
    public int MaxDeliveries { get; private set; }
    public bool IsClosed { get; private set; }

    public void SetMaxDeliveries(int maxDeliveries)
        => MaxDeliveries = maxDeliveries >= 0
            ? maxDeliveries
            : throw new DomainException("Negative", "Max deliveries can't be negative.");

    public void Close() => IsClosed = true;
    public void Reopen() => IsClosed = false;
}
