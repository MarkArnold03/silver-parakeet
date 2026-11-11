using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Parakeet.Application.Customers;
using Parakeet.Application.Geocoding;
using Parakeet.Domain.Common;
using Parakeet.Domain.Deliveries;
using Parakeet.Domain.Orders;

namespace Parakeet.Application.Orders;

public record RecipientRequest(string Name, string Phone, string? Email);
public record AddressRequest(string Street, string Postcode, string City);
public record OrderLineRequest(string ArticleNumber, string Description, int Quantity, decimal UnitVolumeM3, decimal UnitWeightKg);
public record CreateOrderRequest(string MerchantOrderRef, RecipientRequest Recipient, AddressRequest Address,
    DeliveryType DeliveryType, OrderLineRequest[] Lines);

public record OrderDto(long OrderNumber, string MerchantOrderRef, DeliveryStatus Status, GeocodeStatus GeocodeStatus, string CustomerLink);
public record CustomerLinkDto(string CustomerLink);

public class OrderService(IAppDbContext db, GeocodingService geocoding, ICustomerLinks links, TimeProvider time)
{
    /// <summary>
    /// Creates the order and its delivery. Sending the same reference again with the same content
    /// returns the order that already exists (Created = false). The same reference with different
    /// content is refused.
    /// </summary>
    public async Task<(OrderDto Order, bool Created)> CreateAsync(Guid merchantId, CreateOrderRequest request,
        CancellationToken ct = default)
    {
        // Building the domain objects validates and trims the input.
        var recipient = request.Recipient is { } r
            ? new Recipient(r.Name, r.Phone, r.Email)
            : throw new DomainException("Required", "Recipient is required.");
        var address = request.Address is { } a
            ? new Address(a.Street, a.Postcode, a.City)
            : throw new DomainException("Required", "Address is required.");
        var lines = (request.Lines ?? [])
            .Select(l => new OrderLine(l.ArticleNumber, l.Description, l.Quantity, l.UnitVolumeM3, l.UnitWeightKg))
            .ToList();
        var hash = Hash(request.DeliveryType, recipient, address, lines);
        var order = new Order(merchantId, request.MerchantOrderRef, recipient, address, request.DeliveryType, lines, hash,
            time.GetUtcNow());

        if (await FindAsync(merchantId, order.MerchantOrderRef, ct) is { } existing)
            return (SameOrderOrConflict(existing, hash), false);

        await geocoding.LocateAsync(address, ct);

        var delivery = new Delivery(order);
        db.Orders.Add(order);
        db.Deliveries.Add(delivery);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two POSTs with the same reference at the same moment: the unique index let one through.
            // If that is what happened, answer this one like any other retry.
            if (await FindAsync(merchantId, order.MerchantOrderRef, ct) is not { } winner)
                throw;
            return (SameOrderOrConflict(winner, hash), false);
        }

        return (ToDto(order, delivery), true);
    }

    public async Task<OrderDto> GetAsync(Guid merchantId, string merchantOrderRef, CancellationToken ct = default)
        => await FindAsync(merchantId, merchantOrderRef.Trim(), ct) is { } found
            ? ToDto(found.Order, found.Delivery)
            : throw new NotFoundException("OrderNotFound", "There is no order with that reference.");

    /// <summary>Makes every link sent so far stop working and returns a fresh one.</summary>
    public async Task<CustomerLinkDto> RegenerateLinkAsync(Guid deliveryId, CancellationToken ct = default)
    {
        var delivery = await db.Deliveries.SingleOrDefaultAsync(d => d.Id == deliveryId, ct)
            ?? throw new NotFoundException("DeliveryNotFound", "The delivery doesn't exist.");
        delivery.RevokeLinks();
        await db.SaveChangesAsync(ct);
        return new CustomerLinkDto(links.CreateLink(delivery));
    }

    private async Task<(Order Order, Delivery Delivery)?> FindAsync(Guid merchantId, string merchantOrderRef, CancellationToken ct)
    {
        var found = await (
            from order in db.Orders.AsNoTracking()
            join delivery in db.Deliveries.AsNoTracking() on order.Id equals delivery.OrderId
            where order.MerchantId == merchantId && order.MerchantOrderRef == merchantOrderRef
            select new { order, delivery }
        ).SingleOrDefaultAsync(ct);
        return found is null ? null : (found.order, found.delivery);
    }

    private OrderDto SameOrderOrConflict((Order Order, Delivery Delivery) existing, string hash)
        => existing.Order.PayloadHash == hash
            ? ToDto(existing.Order, existing.Delivery)
            : throw new ConflictException("OrderRefReused", "That reference is already used by an order with different content.");

    private OrderDto ToDto(Order order, Delivery delivery)
        => new(order.OrderNumber, order.MerchantOrderRef, delivery.Status, order.Address.GeocodeStatus, links.CreateLink(delivery));

    /// <summary>A fingerprint of everything the merchant sent, after trimming, so "1.5" and "1.50" count as the same.</summary>
    private static string Hash(DeliveryType type, Recipient recipient, Address address, IEnumerable<OrderLine> lines)
    {
        static string N(decimal d) => d.ToString("0.#####", CultureInfo.InvariantCulture);
        var text = new StringBuilder()
            .AppendJoin('\n', type, recipient.Name, recipient.Phone, recipient.Email, address.Street, address.Postcode, address.City);
        foreach (var l in lines)
            text.Append('\n').AppendJoin('\t', l.ArticleNumber, l.Description, l.Quantity, N(l.UnitVolumeM3), N(l.UnitWeightKg));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
}
