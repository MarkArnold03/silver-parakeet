using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Parakeet.Api.Endpoints;
using Parakeet.Domain.Users;
using Parakeet.Infrastructure.Persistence;

namespace Parakeet.IntegrationTests;

/// <summary>
/// Starts the real API in memory against its own fresh database. xUnit creates a new instance
/// of the test class for every test, so every test gets a clean app and database.
/// </summary>
[Collection(SqlServerCollection.Name)]
public abstract class ApiTestBase(SqlServerFixture sql) : IAsyncLifetime
{
    protected const string Email = "disp@example.com";
    protected const string Password = "correct horse battery";
    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private WebApplicationFactory<Program> _factory = null!;
    protected HttpClient Client { get; private set; } = null!;
    protected virtual int LoginsPerMinute => 1000;

    public async Task InitializeAsync()
    {
        var connectionString = await sql.MigratedDatabaseAsync();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseEnvironment("Testing")
            .UseSetting("ConnectionStrings:Default", connectionString)
            .UseSetting("Auth:JwtSigningKey", "integration-test-signing-key-0123456789abcdef")
            .UseSetting("Auth:LoginsPerMinutePerIp", LoginsPerMinute.ToString()));
        // Cookies are handled by hand in the tests, so we can replay an old one on purpose.
        Client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, BaseAddress = new Uri("https://localhost") });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new User(Email, "Test Dispatcher", UserRole.Dispatcher);
        user.SetPasswordHash(scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>().HashPassword(user, Password));
        db.Users.Add(user);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    protected Task<HttpResponseMessage> LoginAsync(string email = Email, string password = Password)
        => Client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));

    /// <summary>Logs in and puts the access token on every following request.</summary>
    protected async Task<string> AuthenticateAsync()
    {
        var response = await LoginAsync();
        var body = await ReadAsync<LoginResponse>(response);
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.AccessToken);
        return RefreshCookieFrom(response);
    }

    protected static string RefreshCookieFrom(HttpResponseMessage response)
        => response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(AuthEndpoints.RefreshCookie + "=")).Split(';')[0];

    protected Task<HttpResponseMessage> PostWithCookieAsync(string url, string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("Cookie", cookie);
        return Client.SendAsync(request);
    }

    protected static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {text}");
        return JsonSerializer.Deserialize<T>(text, Json)!;
    }

    /// <summary>Asserts the status code and returns the "code" from the ProblemDetails body.</summary>
    protected static async Task<string?> ErrorCodeAsync(HttpResponseMessage response, int expectedStatus)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True((int)response.StatusCode == expectedStatus, $"Expected {expectedStatus}, got {(int)response.StatusCode}: {text}");
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
