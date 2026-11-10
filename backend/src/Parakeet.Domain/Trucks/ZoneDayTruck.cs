namespace Parakeet.Domain.Trucks;

/// <summary>"This truck serves this zone on this date." Its capacity counts toward what the zone can sell that day.</summary>
public class ZoneDayTruck
{
    private ZoneDayTruck() { }

    public ZoneDayTruck(Guid zoneId, DateOnly date, Guid truckId)
    {
        ZoneId = zoneId;
        Date = date;
        TruckId = truckId;
    }

    public Guid ZoneId { get; private set; }
    public DateOnly Date { get; private set; }
    public Guid TruckId { get; private set; }
}
