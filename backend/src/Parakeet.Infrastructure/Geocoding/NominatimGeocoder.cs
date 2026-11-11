using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Parakeet.Application.Geocoding;

namespace Parakeet.Infrastructure.Geocoding;

/// <summary>Section "Geocoding" in appsettings.</summary>
public class GeocodingOptions
{
    public string BaseUrl { get; set; } = "https://nominatim.openstreetmap.org";
    /// <summary>Nominatim's usage policy asks every client to say who it is.</summary>
    public string UserAgent { get; set; } = "ParakeetDeliveryBooking/0.1";
    public int TimeoutSeconds { get; set; } = 5;
}

/// <summary>One request per second, shared by the whole process. Nominatim's usage policy requires it.</summary>
public sealed class NominatimThrottle : IDisposable
{
    private readonly TokenBucketRateLimiter _limiter = new(new TokenBucketRateLimiterOptions
    {
        TokenLimit = 1,
        TokensPerPeriod = 1,
        ReplenishmentPeriod = TimeSpan.FromSeconds(1),
        QueueLimit = 20, // more than 20 waiting: give up and flag the address instead of piling up requests
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
    });

    public async Task WaitAsync(CancellationToken ct)
    {
        using var lease = await _limiter.AcquireAsync(1, ct);
        if (!lease.IsAcquired)
            throw new InvalidOperationException("Too many addresses are waiting to be geocoded.");
    }

    public void Dispose() => _limiter.Dispose();
}

public class NominatimGeocoder(HttpClient http, NominatimThrottle throttle) : IGeocoder
{
    public async Task<GeoPoint?> FindAsync(string street, int postcode, string city, CancellationToken ct = default)
    {
        await throttle.WaitAsync(ct);

        var url = "search?format=jsonv2&limit=1&countrycodes=se"
            + $"&street={Uri.EscapeDataString(street)}&postalcode={postcode}&city={Uri.EscapeDataString(city)}";
        var hits = await http.GetFromJsonAsync<List<Hit>>(url, ct) ?? [];
        if (hits.Count == 0)
            return null;

        // Nominatim sends the numbers as text ("59.3293").
        return new GeoPoint(
            double.Parse(hits[0].Lat, CultureInfo.InvariantCulture),
            double.Parse(hits[0].Lon, CultureInfo.InvariantCulture));
    }

    private sealed record Hit([property: JsonPropertyName("lat")] string Lat, [property: JsonPropertyName("lon")] string Lon);
}
