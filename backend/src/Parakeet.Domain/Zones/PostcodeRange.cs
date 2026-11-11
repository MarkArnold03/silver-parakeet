using Parakeet.Domain.Common;

namespace Parakeet.Domain.Zones;

/// <summary>An inclusive range of 5-digit postcodes, e.g. 11100 to 11899.</summary>
public record PostcodeRange
{
    public PostcodeRange(int from, int to)
    {
        if (from < Postcodes.Min || to > Postcodes.Max || from > to)
            throw new DomainException("InvalidPostcodeRange", $"{from}-{to} is not a valid postcode range.");
        From = from;
        To = to;
    }

    public int From { get; }
    public int To { get; }

    public bool Contains(int postcode) => postcode >= From && postcode <= To;
    public bool Overlaps(PostcodeRange other) => From <= other.To && other.From <= To;
}
