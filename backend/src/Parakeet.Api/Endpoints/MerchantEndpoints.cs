using Microsoft.AspNetCore.Mvc;
using Parakeet.Application.Merchants;
using Parakeet.Application.Orders;

namespace Parakeet.Api.Endpoints;

/// <summary>For the shops' own systems. They identify themselves with an API key in the X-Api-Key header.</summary>
public static class MerchantEndpoints
{
    public const string ApiKeyHeader = "X-Api-Key";

    public static void MapMerchantEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/merchant").WithTags("Merchant");

        group.MapPost("/orders", async ([FromHeader(Name = ApiKeyHeader)] string? apiKey, CreateOrderRequest request,
            MerchantService merchants, OrderService orders, CancellationToken ct) =>
        {
            var merchantId = await merchants.AuthenticateAsync(apiKey, ct);
            var (order, created) = await orders.CreateAsync(merchantId, request, ct);
            // 201 the first time, 200 when the same order is sent again.
            return created
                ? Results.Created($"/api/merchant/orders/{Uri.EscapeDataString(order.MerchantOrderRef)}", order)
                : Results.Ok(order);
        }).Produces<OrderDto>(201).Produces<OrderDto>(200);

        group.MapGet("/orders/{merchantOrderRef}", async ([FromHeader(Name = ApiKeyHeader)] string? apiKey, string merchantOrderRef,
            MerchantService merchants, OrderService orders, CancellationToken ct) =>
        {
            var merchantId = await merchants.AuthenticateAsync(apiKey, ct);
            return await orders.GetAsync(merchantId, merchantOrderRef, ct);
        });
    }
}
