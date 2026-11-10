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

    public async Task<ILockedTransaction> BeginLockedTransactionAsync(IEnumerable<string> lockKeys, CancellationToken cancellationToken = default)
    {
        var transaction = await Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Sorted, so every caller takes shared locks in the same order and two callers can never
            // each hold one lock while waiting for the other's (a deadlock).
            foreach (var key in lockKeys.Distinct().Order(StringComparer.Ordinal))
            {
                // sp_getapplock: a named lock in SQL Server. Owner "Transaction" means it is released
                // automatically on commit or rollback, so it can't be left behind by a crash.
                await Database.ExecuteSqlAsync($"""
                    DECLARE @result int;
                    EXEC @result = sp_getapplock @Resource = {key}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000;
                    IF @result < 0 THROW 51000, 'Could not get the lock in time.', 1;
                    """, cancellationToken);
            }
            return new LockedTransaction(transaction);
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    private sealed class LockedTransaction(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction) : ILockedTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken = default) => transaction.CommitAsync(cancellationToken);
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
