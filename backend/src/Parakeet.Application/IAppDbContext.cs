using Microsoft.EntityFrameworkCore;
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

namespace Parakeet.Application;

/// <summary>Disposing without calling Commit rolls everything back.</summary>
public interface ILockedTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);
}

public static class LockKeys
{
    public static string ZoneDay(Guid zoneId, DateOnly date) => $"zoneday:{zoneId:N}:{date:yyyyMMdd}";
    public const string Zones = "zones";
}

/// <summary>What application services see of the database. Implemented by AppDbContext in Infrastructure.</summary>
public interface IAppDbContext
{
    DbSet<Merchant> Merchants { get; }
    DbSet<Order> Orders { get; }
    DbSet<Delivery> Deliveries { get; }
    DbSet<DeliveryEvent> DeliveryEvents { get; }
    DbSet<Signature> Signatures { get; }
    DbSet<Zone> Zones { get; }
    DbSet<Slot> Slots { get; }
    DbSet<Truck> Trucks { get; }
    DbSet<ZoneDayTruck> ZoneDayTrucks { get; }
    DbSet<Route> Routes { get; }
    DbSet<ExtraService> ExtraServices { get; }
    DbSet<DeliveryExtra> DeliveryExtras { get; }
    DbSet<Payment> Payments { get; }
    DbSet<ProcessedStripeEvent> ProcessedStripeEvents { get; }
    DbSet<GeocodeCacheEntry> GeocodeCache { get; }
    DbSet<User> Users { get; }
    DbSet<RefreshToken> RefreshTokens { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a database transaction and takes the named locks. Anyone else asking for one of the same
    /// names waits until this transaction ends. Used where "check, then write" must not be interleaved.
    /// </summary>
    Task<ILockedTransaction> BeginLockedTransactionAsync(IEnumerable<string> lockKeys, CancellationToken cancellationToken = default);
}
