using Microsoft.EntityFrameworkCore;
using Parakeet.Domain.Common;
using Parakeet.Domain.Zones;

namespace Parakeet.Application.Zones;

public record PostcodeRangeDto(int From, int To);
public record ZoneRequest(string Name, PostcodeRangeDto[]? PostcodeRanges);
public record ZoneDto(Guid Id, string Name, PostcodeRangeDto[] PostcodeRanges);

public class ZoneService(IAppDbContext db)
{
    public async Task<IReadOnlyList<ZoneDto>> ListAsync(CancellationToken ct = default)
        => (await db.Zones.AsNoTracking().OrderBy(z => z.Name).ToListAsync(ct)).Select(ToDto).ToList();

    public async Task<ZoneDto> GetAsync(Guid id, CancellationToken ct = default)
        => ToDto(await db.Zones.AsNoTracking().SingleOrDefaultAsync(z => z.Id == id, ct) ?? throw NotFound());

    public Task<ZoneDto> CreateAsync(ZoneRequest request, CancellationToken ct = default)
        => SaveAsync(null, request, ct);

    public Task<ZoneDto> UpdateAsync(Guid id, ZoneRequest request, CancellationToken ct = default)
        => SaveAsync(id, request, ct);

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var zone = await db.Zones.SingleOrDefaultAsync(z => z.Id == id, ct) ?? throw NotFound();
        db.Zones.Remove(zone); // refused by the database (409 InUse) while slots or truck days point at it
        await db.SaveChangesAsync(ct);
    }

    private async Task<ZoneDto> SaveAsync(Guid? id, ZoneRequest request, CancellationToken ct)
    {
        var ranges = (request.PostcodeRanges ?? []).Select(r => new PostcodeRange(r.From, r.To)).ToList();

        // One zone edit at a time, so two edits can't each pass the overlap check and overlap together.
        await using var transaction = await db.BeginLockedTransactionAsync([LockKeys.Zones], ct);
        var zones = await db.Zones.ToListAsync(ct);

        Zone zone;
        if (id is null)
        {
            zone = new Zone(request.Name, ranges);
            db.Zones.Add(zone);
            zones.Add(zone);
        }
        else
        {
            zone = zones.SingleOrDefault(z => z.Id == id) ?? throw NotFound();
            zone.Rename(request.Name);
            zone.SetPostcodeRanges(ranges);
        }
        ZoneRules.EnsureNoOverlap(zones);

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return ToDto(zone);
    }

    private static NotFoundException NotFound() => new("ZoneNotFound", "The zone doesn't exist.");

    private static ZoneDto ToDto(Zone z)
        => new(z.Id, z.Name, z.PostcodeRanges.OrderBy(r => r.From).Select(r => new PostcodeRangeDto(r.From, r.To)).ToArray());
}
