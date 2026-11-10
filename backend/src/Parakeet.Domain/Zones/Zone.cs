using Parakeet.Domain.Common;

namespace Parakeet.Domain.Zones;

public class Zone
{
    private readonly List<PostcodeRange> _postcodeRanges = [];

    private Zone() { }

    public Zone(string name, IEnumerable<PostcodeRange> postcodeRanges)
    {
        Id = Guid.CreateVersion7();
        Rename(name);
        SetPostcodeRanges(postcodeRanges);
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public IReadOnlyCollection<PostcodeRange> PostcodeRanges => _postcodeRanges;

    public void Rename(string name) => Name = Guard.Required(name, nameof(Name), 100);

    public void SetPostcodeRanges(IEnumerable<PostcodeRange> ranges)
    {
        var list = ranges.ToList();
        for (var i = 0; i < list.Count; i++)
            for (var j = i + 1; j < list.Count; j++)
                if (list[i].Overlaps(list[j]))
                    throw new DomainException("PostcodeOverlap",
                        $"Ranges {list[i].From}-{list[i].To} and {list[j].From}-{list[j].To} overlap inside zone '{Name}'.");
        _postcodeRanges.Clear();
        _postcodeRanges.AddRange(list);
    }

    public bool Covers(int postcode) => _postcodeRanges.Any(r => r.Contains(postcode));
}
