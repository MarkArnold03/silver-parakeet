using Parakeet.Domain.Common;

namespace Parakeet.Domain.Zones;

public static class ZoneRules
{
    /// <summary>A postcode must belong to at most one zone. Call with all zones before saving a change.</summary>
    public static void EnsureNoOverlap(IEnumerable<Zone> zones)
    {
        var all = zones.SelectMany(z => z.PostcodeRanges.Select(r => (Zone: z, Range: r))).ToList();
        for (var i = 0; i < all.Count; i++)
            for (var j = i + 1; j < all.Count; j++)
                if (all[i].Zone != all[j].Zone && all[i].Range.Overlaps(all[j].Range))
                    throw new DomainException("PostcodeOverlap",
                        $"Zone '{all[i].Zone.Name}' ({all[i].Range.From}-{all[i].Range.To}) overlaps " +
                        $"zone '{all[j].Zone.Name}' ({all[j].Range.From}-{all[j].Range.To}).");
    }

    public static Zone? FindZone(IEnumerable<Zone> zones, int postcode)
        => zones.FirstOrDefault(z => z.Covers(postcode));
}
