using Parakeet.Domain.Common;

namespace Parakeet.Domain.Zones;

public static class Postcodes
{
    public const int Min = 10000;
    public const int Max = 99999;

    /// <summary>Accepts "11455", "114 55" and "SE-114 55". Returns 11455.</summary>
    public static int Parse(string? text)
    {
        var digits = new string((text ?? "").Where(char.IsAsciiDigit).ToArray());
        if (digits.Length != 5 || !int.TryParse(digits, out var postcode) || postcode < Min)
            throw new DomainException("InvalidPostcode", $"'{text}' is not a Swedish postcode (5 digits).");
        return postcode;
    }
}
