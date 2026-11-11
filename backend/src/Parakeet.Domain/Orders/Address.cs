using Parakeet.Domain.Common;
using Parakeet.Domain.Zones;

namespace Parakeet.Domain.Orders;

public enum GeocodeStatus { Pending, Ok, Failed }

public class Address
{
    private Address() { }

    public Address(string street, string postcode, string city)
    {
        Street = Guard.Required(street, nameof(Street), 200);
        Postcode = Postcodes.Parse(postcode);
        City = Guard.Required(city, nameof(City), 100);
    }

    public string Street { get; private set; } = null!;
    /// <summary>Swedish postcode as a 5-digit number ("114 55" is stored as 11455).</summary>
    public int Postcode { get; private set; }
    public string City { get; private set; } = null!;
    public double? Lat { get; private set; }
    public double? Lng { get; private set; }
    public GeocodeStatus GeocodeStatus { get; private set; } = GeocodeStatus.Pending;

    public void SetLocation(double lat, double lng)
    {
        (Lat, Lng, GeocodeStatus) = (lat, lng, GeocodeStatus.Ok);
    }

    public void MarkGeocodeFailed()
    {
        (Lat, Lng, GeocodeStatus) = (null, null, GeocodeStatus.Failed);
    }
}
