using Microsoft.EntityFrameworkCore;
using Parakeet.Domain.Common;
using Parakeet.Domain.Trucks;

namespace Parakeet.Application.Trucks;

public record TruckRequest(string Name, string Plate, decimal CapacityM3, decimal CapacityKg, bool IsActive = true);
public record TruckDto(Guid Id, string Name, string Plate, decimal CapacityM3, decimal CapacityKg, bool IsActive);

public class TruckService(IAppDbContext db)
{
    public async Task<IReadOnlyList<TruckDto>> ListAsync(CancellationToken ct = default)
        => await db.Trucks.AsNoTracking().OrderBy(t => t.Name).Select(t => ToDto(t)).ToListAsync(ct);

    public async Task<TruckDto> GetAsync(Guid id, CancellationToken ct = default)
        => ToDto(await db.Trucks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, ct) ?? throw NotFound());

    public async Task<TruckDto> CreateAsync(TruckRequest request, CancellationToken ct = default)
    {
        var truck = new Truck(request.Name, request.Plate, request.CapacityM3, request.CapacityKg);
        if (!request.IsActive) truck.Deactivate();
        db.Trucks.Add(truck);
        await db.SaveChangesAsync(ct);
        return ToDto(truck);
    }

    /// <summary>
    /// Trucks are deactivated, never deleted. A smaller or deactivated truck can leave a zone-day oversold,
    /// the same as taking it off the day: dispatch sees the warning and deals with it.
    /// </summary>
    public async Task<TruckDto> UpdateAsync(Guid id, TruckRequest request, CancellationToken ct = default)
    {
        var truck = await db.Trucks.SingleOrDefaultAsync(t => t.Id == id, ct) ?? throw NotFound();
        truck.Update(request.Name, request.Plate, request.CapacityM3, request.CapacityKg);
        if (request.IsActive) truck.Activate(); else truck.Deactivate();
        await db.SaveChangesAsync(ct);
        return ToDto(truck);
    }

    private static NotFoundException NotFound() => new("TruckNotFound", "The truck doesn't exist.");
    private static TruckDto ToDto(Truck t) => new(t.Id, t.Name, t.Plate, t.CapacityM3, t.CapacityKg, t.IsActive);
}
