using Microsoft.EntityFrameworkCore;
using Parakeet.Application;
using Parakeet.Domain.Deliveries;
using Parakeet.Domain.Geocoding;
using Parakeet.Domain.Merchants;
using Parakeet.Domain.Orders;
using Parakeet.Domain.Payments;
using Parakeet.Domain.Routes;
using Parakeet.Domain.Slots;
using Parakeet.Domain.Trucks;
using Parakeet.Domain.Users;
using Parakeet.Domain.Zones;

namespace Parakeet.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<Merchant> Merchants => Set<Merchant>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Delivery> Deliveries => Set<Delivery>();
    public DbSet<DeliveryEvent> DeliveryEvents => Set<DeliveryEvent>();
    public DbSet<Signature> Signatures => Set<Signature>();
    public DbSet<Zone> Zones => Set<Zone>();
    public DbSet<Slot> Slots => Set<Slot>();
    public DbSet<Truck> Trucks => Set<Truck>();
    public DbSet<ZoneDayTruck> ZoneDayTrucks => Set<ZoneDayTruck>();
    public DbSet<Route> Routes => Set<Route>();
    public DbSet<ExtraService> ExtraServices => Set<ExtraService>();
    public DbSet<DeliveryExtra> DeliveryExtras => Set<DeliveryExtra>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<ProcessedStripeEvent> ProcessedStripeEvents => Set<ProcessedStripeEvent>();
    public DbSet<GeocodeCacheEntry> GeocodeCache => Set<GeocodeCacheEntry>();
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Enums are stored as their names ("Booked"), not numbers, so the tables are readable
        // and reordering an enum in code can never change what stored data means.
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(32);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder) => ModelConfiguration.Apply(modelBuilder);
}
