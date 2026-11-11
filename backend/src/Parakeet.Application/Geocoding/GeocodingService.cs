using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Parakeet.Domain.Geocoding;
using Parakeet.Domain.Orders;

namespace Parakeet.Application.Geocoding;

public record GeoPoint(double Lat, double Lng);

/// <summary>Turns an address into coordinates. Implemented by Nominatim in Infrastructure, faked in tests.</summary>
public interface IGeocoder
{
    /// <summary>Null means "no such address". Technical problems (timeout, server error) throw.</summary>
    Task<GeoPoint?> FindAsync(string street, int postcode, string city, CancellationToken ct = default);
}

public class GeocodingService(IAppDbContext db, IGeocoder geocoder, TimeProvider time, ILogger<GeocodingService> logger)
{
    public static string KeyFor(Address address)
        => $"{address.Street}|{address.Postcode}|{address.City}".ToLowerInvariant();

    /// <summary>
    /// Sets the coordinates on the address, or flags it as failed. Never throws: a geocoder that is
    /// down must not stop an order from being created.
    /// </summary>
    public async Task LocateAsync(Address address, CancellationToken ct = default)
    {
        var key = KeyFor(address);
        var cached = await db.GeocodeCache.AsNoTracking().SingleOrDefaultAsync(c => c.AddressKey == key, ct);
        if (cached is not null)
        {
            Apply(address, cached.Found ? new GeoPoint(cached.Lat!.Value, cached.Lng!.Value) : null);
            return;
        }

        GeoPoint? point;
        try
        {
            point = await geocoder.FindAsync(address.Street, address.Postcode, address.City, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Not cached, so the next attempt for this address asks again.
            logger.LogWarning(e, "Geocoding failed for an address in {Postcode}", address.Postcode);
            address.MarkGeocodeFailed();
            return;
        }

        Apply(address, point);
        var entry = new GeocodeCacheEntry(key, point?.Lat, point?.Lng, time.GetUtcNow());
        db.GeocodeCache.Add(entry);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Another request cached the same address a moment ago. Nothing to do.
            db.GeocodeCache.Entry(entry).State = EntityState.Detached;
        }
    }

    private static void Apply(Address address, GeoPoint? point)
    {
        if (point is null)
            address.MarkGeocodeFailed();
        else
            address.SetLocation(point.Lat, point.Lng);
    }
}
