namespace Parakeet.Domain.Geocoding;

/// <summary>Remembers the answer for an address so the geocoder is asked once per address.</summary>
public class GeocodeCacheEntry
{
    private GeocodeCacheEntry() { }

    public GeocodeCacheEntry(string addressKey, double? lat, double? lng, DateTimeOffset at)
    {
        AddressKey = addressKey;
        Lat = lat;
        Lng = lng;
        Found = lat is not null && lng is not null;
        At = at;
    }

    /// <summary>Normalized "street|postcode|city".</summary>
    public string AddressKey { get; private set; } = null!;
    public double? Lat { get; private set; }
    public double? Lng { get; private set; }
    public bool Found { get; private set; }
    public DateTimeOffset At { get; private set; }
}
