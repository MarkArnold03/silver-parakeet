using System.Security.Claims;
using Parakeet.Application.Customers;
using Parakeet.Domain.Common;

namespace Parakeet.Api.Endpoints;

/// <summary>For the customer app. The token from the customer's link says which delivery this is about.</summary>
public static class CustomerEndpoints
{
    /// <summary>Name of both the authentication scheme and the authorization policy for customer tokens.</summary>
    public const string Customer = "Customer";

    public static void MapCustomerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/customer").WithTags("Customer").RequireAuthorization(Customer);

        group.MapGet("/delivery", (ClaimsPrincipal user, CustomerDeliveryService s, CancellationToken ct)
            => s.GetAsync(AccessOf(user), ct));

        group.MapGet("/slots", (DateOnly? from, DateOnly? to, ClaimsPrincipal user, CustomerDeliveryService s, CancellationToken ct)
            => s.GetSlotsAsync(AccessOf(user), from, to, ct));

        group.MapPut("/delivery/slot", (BookSlotRequest request, ClaimsPrincipal user, CustomerDeliveryService s, CancellationToken ct)
            => s.BookAsync(AccessOf(user), request.SlotId, ct));
    }

    private static CustomerAccess AccessOf(ClaimsPrincipal user)
        => Guid.TryParse(user.FindFirstValue(CustomerLinkOptions.DeliveryIdClaim), out var deliveryId)
            && int.TryParse(user.FindFirstValue(CustomerLinkOptions.LinkVersionClaim), out var linkVersion)
            ? new CustomerAccess(deliveryId, linkVersion)
            : throw new UnauthorizedException("LinkInvalid", "This link doesn't work.");
}
