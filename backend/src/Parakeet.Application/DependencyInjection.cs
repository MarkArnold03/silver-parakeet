using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Parakeet.Application.Auth;
using Parakeet.Application.Booking;
using Parakeet.Application.Customers;
using Parakeet.Application.Geocoding;
using Parakeet.Application.Merchants;
using Parakeet.Application.Orders;
using Parakeet.Application.Slots;
using Parakeet.Application.Trucks;
using Parakeet.Application.Zones;
using Parakeet.Domain.Users;

namespace Parakeet.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<BookingService>();
        services.AddScoped<ZoneService>();
        services.AddScoped<TruckService>();
        services.AddScoped<SlotService>();
        services.AddScoped<AuthService>();
        services.AddScoped<GeocodingService>();
        services.AddScoped<OrderService>();
        services.AddScoped<MerchantService>();
        services.AddScoped<CustomerDeliveryService>();
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        return services;
    }
}
