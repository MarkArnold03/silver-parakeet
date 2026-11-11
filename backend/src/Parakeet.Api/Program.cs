using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Parakeet.Api;
using Parakeet.Api.Endpoints;
using Parakeet.Application;
using Parakeet.Application.Auth;
using Parakeet.Application.Booking;
using Parakeet.Application.Customers;
using Parakeet.Domain.Users;
using Parakeet.Infrastructure;
using Parakeet.Infrastructure.Auth;
using Parakeet.Infrastructure.Geocoding;
using Parakeet.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Connection string 'Default' is missing.");
var auth = builder.Configuration.GetSection("Auth").Get<AuthOptions>() ?? new AuthOptions();
var customerLinks = builder.Configuration.GetSection("CustomerLinks").Get<CustomerLinkOptions>() ?? new CustomerLinkOptions();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(connectionString);
builder.Services.Configure<BookingOptions>(builder.Configuration.GetSection("Booking"));
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection("Auth"));
builder.Services.Configure<CustomerLinkOptions>(builder.Configuration.GetSection("CustomerLinks"));
builder.Services.Configure<GeocodingOptions>(builder.Configuration.GetSection("Geocoding"));

builder.Services.AddOpenApi();
builder.Services.AddValidation();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ErrorHandler>();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Dispatchers prove who they are with a signed access token (JWT) on every request.
// Customers prove which delivery they may see with the token from their link. It is a second, separate
// scheme with its own secret and audience, so a customer token is never accepted as a dispatcher.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(CustomerEndpoints.Customer, o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = AuthOptions.Issuer,
            ValidAudience = CustomerLinkOptions.Audience,
            IssuerSigningKey = JwtAccessTokenIssuer.KeyFrom(customerLinks.SigningKey, "CustomerLinks:SigningKey"),
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    })
    .AddJwtBearer(o =>
{
    o.MapInboundClaims = false; // keep the claim names exactly as we wrote them: sub, name, role
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = AuthOptions.Issuer,
        ValidAudience = AuthOptions.DispatchAudience,
        IssuerSigningKey = JwtAccessTokenIssuer.KeyFrom(auth.JwtSigningKey),
        NameClaimType = "sub",
        RoleClaimType = "role",
        ClockSkew = TimeSpan.FromSeconds(30),
    };
});
builder.Services.AddAuthorizationBuilder().AddPolicy(CustomerEndpoints.Customer, p => p
    .AddAuthenticationSchemes(CustomerEndpoints.Customer)
    .RequireClaim(CustomerLinkOptions.DeliveryIdClaim));

// Slows down password guessing: a fixed number of login attempts per minute from one address.
// ponytail: behind a reverse proxy every request has the proxy's address. Add forwarded headers when we deploy.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(AuthEndpoints.LoginRateLimit, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = auth.LoginsPerMinutePerIp, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi(); // the API description, at /openapi/v1.json

    // Local convenience only: bring the database up to date and add sample data.
    // Production migrations are a deliberate deploy step, never something the app does on start.
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await DevSeeder.SeedAsync(db, scope.ServiceProvider.GetRequiredService<TimeProvider>(),
        scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>());
}

app.UseExceptionHandler();
app.UseStatusCodePages(); // gives bare 401/404/429 responses a ProblemDetails body too
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", async (AppDbContext db) =>
    await db.Database.CanConnectAsync() ? Results.Ok(new { status = "ok" }) : Results.StatusCode(503));
app.MapAuthEndpoints();
app.MapDispatchEndpoints();
app.MapMerchantEndpoints();
app.MapCustomerEndpoints();

app.Run();

public partial class Program; // lets the integration tests start the API in-process
