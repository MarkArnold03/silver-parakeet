using System.Diagnostics;
using System.Net;
using System.Text;
using Parakeet.Infrastructure.Geocoding;

namespace Parakeet.IntegrationTests;

/// <summary>The real Nominatim client against a pretend web server. No database and no internet needed.</summary>
public class NominatimGeocoderTests
{
    private sealed class FakeServer(string json) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }

    private static (NominatimGeocoder Geocoder, FakeServer Server) Create(string json)
    {
        var server = new FakeServer(json);
        var http = new HttpClient(server) { BaseAddress = new Uri("https://geocoder.test/") };
        return (new NominatimGeocoder(http, new NominatimThrottle()), server);
    }

    [Fact]
    public async Task Reads_the_coordinates_from_the_first_hit()
    {
        var (geocoder, server) = Create("""[{"lat":"59.3364","lon":"18.0628","display_name":"Sveavägen 44"}]""");

        var point = await geocoder.FindAsync("Sveavägen 44", 11134, "Stockholm");

        Assert.Equal((59.3364, 18.0628), (point!.Lat, point.Lng));
        var query = Assert.Single(server.Requests).Query;
        Assert.Contains("street=Sveav%C3%A4gen%2044", query);
        Assert.Contains("postalcode=11134", query);
        Assert.Contains("city=Stockholm", query);
    }

    [Fact]
    public async Task No_hits_means_the_address_was_not_found()
    {
        var (geocoder, _) = Create("[]");

        Assert.Null(await geocoder.FindAsync("Finns Inte 1", 11134, "Stockholm"));
    }

    [Fact]
    public async Task The_second_request_waits_so_we_send_at_most_one_per_second()
    {
        var (geocoder, _) = Create("[]");
        await geocoder.FindAsync("A 1", 11134, "Stockholm");

        var watch = Stopwatch.StartNew();
        await geocoder.FindAsync("B 2", 11134, "Stockholm");

        Assert.InRange(watch.Elapsed, TimeSpan.FromMilliseconds(800), TimeSpan.FromSeconds(5));
    }
}
