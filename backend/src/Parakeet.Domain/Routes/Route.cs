namespace Parakeet.Domain.Routes;

/// <summary>One truck's run on one date. Its stops are the deliveries pointing at it, ordered by StopSequence.</summary>
public class Route
{
    private Route() { }

    public Route(Guid truckId, DateOnly date)
    {
        Id = Guid.CreateVersion7();
        TruckId = truckId;
        Date = date;
    }

    public Guid Id { get; private set; }
    public Guid TruckId { get; private set; }
    public DateOnly Date { get; private set; }
    /// <summary>Always 1 for now. Kept so several runs per truck and day can be added without a migration.</summary>
    public int RunNumber { get; private set; } = 1;
}
