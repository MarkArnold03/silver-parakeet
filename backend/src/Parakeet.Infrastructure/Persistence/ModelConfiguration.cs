using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
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

/// <summary>How the domain classes map to SQL Server tables: sizes, unique indexes and relationships.</summary>
internal static class ModelConfiguration
{
    public const string OrderNumberSequence = "OrderNumbers";

    public static void Apply(ModelBuilder b)
    {
        Merchants(b);
        Orders(b);
        Deliveries(b);
        ZonesAndSlots(b);
        TrucksAndRoutes(b);
        Payments(b);
        Misc(b);
        Conventions(b);
    }

    private static void Merchants(ModelBuilder b) => b.Entity<Merchant>(e =>
    {
        e.Property(x => x.Name).HasMaxLength(200);
        e.Property(x => x.ApiKeyHash).HasMaxLength(128);
        e.Property(x => x.ApiKeyPrefix).HasMaxLength(16);
        e.HasIndex(x => x.ApiKeyHash).IsUnique();
    });

    private static void Orders(ModelBuilder b)
    {
        b.HasSequence<long>(OrderNumberSequence).StartsAt(100000);

        b.Entity<Order>(e =>
        {
            e.Property(x => x.MerchantOrderRef).HasMaxLength(100);
            e.Property(x => x.PayloadHash).HasMaxLength(64).IsUnicode(false);
            e.Property(x => x.OrderNumber).HasDefaultValueSql($"NEXT VALUE FOR {OrderNumberSequence}");
            e.HasIndex(x => x.OrderNumber).IsUnique();
            // Idempotency: the database itself refuses a second order with the same merchant reference.
            e.HasIndex(x => new { x.MerchantId, x.MerchantOrderRef }).IsUnique();
            e.HasOne<Merchant>().WithMany().HasForeignKey(x => x.MerchantId);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.OrderId);

            e.OwnsOne(x => x.Recipient, r =>
            {
                r.Property(x => x.Name).HasMaxLength(200);
                r.Property(x => x.Phone).HasMaxLength(32);
                r.Property(x => x.Email).HasMaxLength(254);
            });
            e.Navigation(x => x.Recipient).IsRequired();

            e.OwnsOne(x => x.Address, a =>
            {
                a.Property(x => x.Street).HasMaxLength(200);
                a.Property(x => x.City).HasMaxLength(100);
                a.HasIndex(x => x.Postcode);
            });
            e.Navigation(x => x.Address).IsRequired();
        });

        b.Entity<OrderLine>(e =>
        {
            e.ToTable("OrderLines");
            e.Property(x => x.ArticleNumber).HasMaxLength(64);
            e.Property(x => x.Description).HasMaxLength(300);
            e.Property(x => x.UnitVolumeM3).HasPrecision(9, 3);
            e.Property(x => x.UnitWeightKg).HasPrecision(9, 2);
        });
    }

    private static void Deliveries(ModelBuilder b)
    {
        b.Entity<Delivery>(e =>
        {
            e.Property(x => x.VolumeM3).HasPrecision(9, 3);
            e.Property(x => x.WeightKg).HasPrecision(9, 2);
            e.Property(x => x.FailureReason).HasMaxLength(500);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasIndex(x => x.OrderId).IsUnique(); // one delivery per order, for now
            e.HasIndex(x => x.Status);
            e.HasIndex(x => new { x.RouteId, x.StopSequence });
            e.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId);
            e.HasOne<Slot>().WithMany().HasForeignKey(x => x.SlotId);
            e.HasOne<Route>().WithMany().HasForeignKey(x => x.RouteId);
            e.HasMany(x => x.Events).WithOne().HasForeignKey(x => x.DeliveryId);

            e.OwnsOne(x => x.AlternativeRecipient, r =>
            {
                r.Property(x => x.Name).HasMaxLength(200);
                r.Property(x => x.Phone).HasMaxLength(32);
            });
        });

        b.Entity<DeliveryEvent>(e =>
        {
            e.Property(x => x.ActorId).HasMaxLength(64);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.HasIndex(x => new { x.DeliveryId, x.At });
        });

        b.Entity<Signature>(e =>
        {
            e.Property(x => x.SignerName).HasMaxLength(200);
            e.HasIndex(x => new { x.DeliveryId, x.Kind }).IsUnique();
            e.HasOne<Delivery>().WithMany().HasForeignKey(x => x.DeliveryId);
        });
    }

    private static void ZonesAndSlots(ModelBuilder b)
    {
        b.Entity<Zone>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.HasIndex(x => x.Name).IsUnique();
            e.OwnsMany(x => x.PostcodeRanges, r =>
            {
                r.ToTable("PostcodeRanges");
                r.WithOwner().HasForeignKey("ZoneId");
                r.Property<int>("Id");
                r.HasKey("Id");
                r.Property(x => x.From).HasColumnName("FromPostcode");
                r.Property(x => x.To).HasColumnName("ToPostcode");
            });
        });

        b.Entity<Slot>(e =>
        {
            e.HasIndex(x => new { x.ZoneId, x.Date, x.Start }).IsUnique();
            e.HasOne<Zone>().WithMany().HasForeignKey(x => x.ZoneId);
        });
    }

    private static void TrucksAndRoutes(ModelBuilder b)
    {
        b.Entity<Truck>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Plate).HasMaxLength(16);
            e.Property(x => x.CapacityM3).HasPrecision(9, 3);
            e.Property(x => x.CapacityKg).HasPrecision(9, 2);
            e.HasIndex(x => x.Plate).IsUnique();
        });

        b.Entity<ZoneDayTruck>(e =>
        {
            e.HasKey(x => new { x.ZoneId, x.Date, x.TruckId });
            e.HasIndex(x => new { x.TruckId, x.Date }).IsUnique(); // a truck serves one zone per day
            e.HasOne<Zone>().WithMany().HasForeignKey(x => x.ZoneId);
            e.HasOne<Truck>().WithMany().HasForeignKey(x => x.TruckId);
        });

        b.Entity<Route>(e =>
        {
            e.HasIndex(x => new { x.TruckId, x.Date, x.RunNumber }).IsUnique();
            e.HasOne<Truck>().WithMany().HasForeignKey(x => x.TruckId);
        });
    }

    private static void Payments(ModelBuilder b)
    {
        b.Entity<ExtraService>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(50);
            e.Property(x => x.NameSv).HasMaxLength(100);
            e.Property(x => x.NameEn).HasMaxLength(100);
            e.HasIndex(x => x.Code).IsUnique();
        });

        b.Entity<DeliveryExtra>(e =>
        {
            e.HasIndex(x => new { x.DeliveryId, x.ExtraServiceId }).IsUnique();
            e.HasOne<Delivery>().WithMany().HasForeignKey(x => x.DeliveryId);
            e.HasOne<ExtraService>().WithMany().HasForeignKey(x => x.ExtraServiceId);
            e.HasOne<Payment>().WithMany().HasForeignKey(x => x.PaymentId);
        });

        b.Entity<Payment>(e =>
        {
            e.Property(x => x.StripeCheckoutSessionId).HasMaxLength(255);
            e.HasIndex(x => x.StripeCheckoutSessionId).IsUnique();
            e.HasOne<Delivery>().WithMany().HasForeignKey(x => x.DeliveryId);
        });

        b.Entity<ProcessedStripeEvent>(e =>
        {
            e.HasKey(x => x.EventId);
            e.Property(x => x.EventId).HasMaxLength(255);
        });
    }

    private static void Misc(ModelBuilder b)
    {
        b.Entity<GeocodeCacheEntry>(e =>
        {
            e.ToTable("GeocodeCache");
            e.HasKey(x => x.AddressKey);
            e.Property(x => x.AddressKey).HasMaxLength(400);
        });

        b.Entity<User>(e =>
        {
            e.Property(x => x.Email).HasMaxLength(254);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.PasswordHash).HasMaxLength(500);
            e.HasIndex(x => x.Email).IsUnique();
        });

        b.Entity<RefreshToken>(e =>
        {
            e.Property(x => x.TokenHash).HasMaxLength(64).IsUnicode(false);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
            e.HasOne<RefreshToken>().WithMany().HasForeignKey(x => x.ReplacedById);
        });
    }

    private static void Conventions(ModelBuilder b)
    {
        foreach (var entity in b.Model.GetEntityTypes())
        {
            // Ids are created in the constructors (Guid v7). Without this EF assumes that a new object with
            // an id already set is an existing row, and tries to UPDATE it instead of INSERTing it.
            foreach (var key in entity.GetKeys().SelectMany(k => k.Properties).Where(p => p.ClrType == typeof(Guid)))
                key.ValueGenerated = ValueGenerated.Never;

            // Deleting a row never silently deletes other rows. The exception is data that is part of
            // its owner (a zone's postcode ranges).
            foreach (var fk in entity.GetForeignKeys().Where(fk => !fk.IsOwnership))
                fk.DeleteBehavior = DeleteBehavior.Restrict;
        }
    }
}
