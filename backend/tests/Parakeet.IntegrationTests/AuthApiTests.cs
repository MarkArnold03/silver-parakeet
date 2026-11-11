using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Parakeet.Api.Endpoints;
using Parakeet.Domain.Users;

namespace Parakeet.IntegrationTests;

public class AuthApiTests(SqlServerFixture sql) : ApiTestBase(sql)
{
    [Fact]
    public async Task Dispatch_endpoints_refuse_callers_without_a_valid_token()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.GetAsync("/api/trucks")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.GetAsync("/api/zone-days?from=2026-11-12&to=2026-11-12")).StatusCode);

        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-token");
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.GetAsync("/api/trucks")).StatusCode);

        // A real token with one character changed has a broken signature.
        var body = await ReadAsync<LoginResponse>(await LoginAsync());
        var last = body.AccessToken[^1];
        var tampered = body.AccessToken[..^1] + (last == 'A' ? 'B' : 'A');
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tampered);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.GetAsync("/api/trucks")).StatusCode);
    }

    [Fact]
    public async Task Login_returns_a_working_token_and_an_http_only_cookie()
    {
        var response = await LoginAsync(" DISP@example.com "); // email is trimmed and case-insensitive
        var body = await ReadAsync<LoginResponse>(response);

        Assert.Equal((Email, UserRole.Dispatcher), (body.User.Email, body.User.Role));
        var cookie = response.Headers.GetValues("Set-Cookie").Single();
        Assert.Contains("httponly", cookie);
        Assert.Contains("secure", cookie);
        Assert.Contains("samesite=strict", cookie);
        Assert.Contains("path=/api/auth", cookie);
        Assert.DoesNotContain(RefreshCookieFrom(response).Split('=', 2)[1], await response.Content.ReadAsStringAsync());

        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync("/api/trucks")).StatusCode);
    }

    [Theory]
    [InlineData(Email, "wrong password")]
    [InlineData("nobody@example.com", Password)]
    public async Task Wrong_password_and_unknown_email_get_the_same_answer(string email, string password)
        => Assert.Equal("InvalidCredentials", await ErrorCodeAsync(await LoginAsync(email, password), 401));

    [Fact]
    public async Task Login_without_email_or_password_is_a_bad_request()
        => Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync("/api/auth/login", new { })).StatusCode);

    [Fact]
    public async Task Refresh_swaps_the_token_and_reusing_an_old_one_ends_the_whole_session()
    {
        var first = await AuthenticateAsync();

        var refreshed = await PostWithCookieAsync("/api/auth/refresh", first);
        var second = RefreshCookieFrom(refreshed);
        await ReadAsync<LoginResponse>(refreshed);
        Assert.NotEqual(first, second);

        var third = RefreshCookieFrom(await PostWithCookieAsync("/api/auth/refresh", second));

        // Someone replays the very first token. It was already used, so it is refused...
        Assert.Equal("RefreshTokenReused", await ErrorCodeAsync(await PostWithCookieAsync("/api/auth/refresh", first), 401));
        // ...and the newest token, which was valid until now, is dead as well.
        Assert.Equal("RefreshTokenReused", await ErrorCodeAsync(await PostWithCookieAsync("/api/auth/refresh", third), 401));
    }

    [Fact]
    public async Task Two_refreshes_with_the_same_token_at_the_same_time_give_one_new_session()
    {
        var cookie = await AuthenticateAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => PostWithCookieAsync("/api/auth/refresh", cookie)));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(5, responses.Count(r => r.StatusCode == HttpStatusCode.Unauthorized));
    }

    [Fact]
    public async Task Refresh_without_or_with_an_unknown_cookie_is_refused()
    {
        Assert.Equal("InvalidRefreshToken", await ErrorCodeAsync(await Client.PostAsync("/api/auth/refresh", null), 401));
        Assert.Equal("InvalidRefreshToken", await ErrorCodeAsync(
            await PostWithCookieAsync("/api/auth/refresh", $"{AuthEndpoints.RefreshCookie}=made-up"), 401));
    }

    [Fact]
    public async Task Logout_makes_the_refresh_token_useless()
    {
        var cookie = await AuthenticateAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await PostWithCookieAsync("/api/auth/logout", cookie)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await PostWithCookieAsync("/api/auth/refresh", cookie)).StatusCode);
    }
}

public class LoginRateLimitTests(SqlServerFixture sql) : ApiTestBase(sql)
{
    protected override int LoginsPerMinute => 3;

    [Fact]
    public async Task Too_many_login_attempts_are_slowed_down()
    {
        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(password: "guess")).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await LoginAsync(password: "guess")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await LoginAsync()).StatusCode); // even the right password has to wait
    }
}
