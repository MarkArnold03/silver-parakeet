using Parakeet.Domain.Common;
using Parakeet.Domain.Zones;

namespace Parakeet.UnitTests;

public class ZoneTests
{
    private static Zone Zone(string name, params (int From, int To)[] ranges)
        => new(name, ranges.Select(r => new PostcodeRange(r.From, r.To)));

    [Theory]
    [InlineData("11455", 11455)]
    [InlineData("114 55", 11455)]
    [InlineData(" SE-114 55 ", 11455)]
    public void Postcode_is_parsed_from_common_spellings(string text, int expected)
        => Assert.Equal(expected, Postcodes.Parse(text));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("1145")]
    [InlineData("114556")]
    [InlineData("01234")]
    [InlineData("abcde")]
    public void Bad_postcodes_are_rejected(string? text)
        => Assert.Equal("InvalidPostcode", Assert.ThrowsAny<DomainException>(() => Postcodes.Parse(text)).Code);

    [Theory]
    [InlineData(9999, 11000)]
    [InlineData(11000, 100000)]
    [InlineData(12000, 11000)]
    public void Bad_ranges_are_rejected(int from, int to)
        => Assert.ThrowsAny<DomainException>(() => new PostcodeRange(from, to));

    [Theory]
    [InlineData(11100, true)]   // first postcode in the range
    [InlineData(11899, true)]   // last postcode in the range: ranges are inclusive
    [InlineData(11099, false)]
    [InlineData(11900, false)]
    [InlineData(12050, true)]   // second range
    public void Zone_covers_postcodes_inside_its_ranges(int postcode, bool expected)
        => Assert.Equal(expected, Zone("City", (11100, 11899), (12000, 12099)).Covers(postcode));

    [Theory]
    [InlineData(11000, 11100)]  // touches the first postcode
    [InlineData(11899, 12500)]  // touches the last postcode
    [InlineData(11300, 11400)]  // inside
    [InlineData(10000, 99999)]  // around
    public void Overlap_between_zones_is_rejected(int from, int to)
    {
        var ex = Assert.ThrowsAny<DomainException>(() =>
            ZoneRules.EnsureNoOverlap([Zone("City", (11100, 11899)), Zone("Other", (from, to))]));
        Assert.Equal("PostcodeOverlap", ex.Code);
    }

    [Fact]
    public void Neighbouring_ranges_do_not_overlap()
        => ZoneRules.EnsureNoOverlap([Zone("City", (11100, 11899)), Zone("South", (11900, 12999)), Zone("Empty")]);

    [Fact]
    public void Overlap_inside_one_zone_is_rejected()
        => Assert.ThrowsAny<DomainException>(() => Zone("City", (11100, 11899), (11500, 12000)));

    [Fact]
    public void Lookup_finds_the_zone_for_a_postcode_or_nothing()
    {
        Zone city = Zone("City", (11100, 11899)), south = Zone("South", (11900, 12999));

        Assert.Same(city, ZoneRules.FindZone([city, south], Postcodes.Parse("114 55")));
        Assert.Same(south, ZoneRules.FindZone([city, south], 11900));
        Assert.Null(ZoneRules.FindZone([city, south], 41101));
    }
}
