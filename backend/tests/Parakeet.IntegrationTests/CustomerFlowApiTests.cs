using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Parakeet.Application.Booking;
using Parakeet.Application.Customers;
using Parakeet.Application.Merchants;
using Parakeet.Application.Orders;
using Parakeet.Domain.Common;
using Parakeet.Domain.Deliveries;
using Parakeet.Domain.Orders;
using Parakeet.Domain.Slots;
using Parakeet.Domain.Trucks;
using Parakeet.Domain.Zones;
using Parakeet.Infrastructure.Persistence;

namespace Parakeet.IntegrationTests;

/// <summary>The whole chain through the real API: a shop sends an order, the customer opens the link and books.</summary>
public class CustomerFlowApiTests(SqlServerFixture sql) : ApiTestBase(sql)
{
    // The API runs on the real clock here, so dates are relative to today. Three days ahead is safely before any cutoff.
    private static readonly DateOnly Today = StockholmTime.Today(DateTimeOffset.UtcNow);
    private static readonly DateOnly DeliveryDay = Today.AddDays(3);

    private string _apiKey = null!;

    private async Task<T> DbAsync<T>(Func<AppDbContext, Task<T>> work)
    {
        using var scope = Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    /// <summary>A merchant with a key, zone "City" (11100-11899), one truck and one slot on the delivery day.</summary>
    private async Task<Slot> ArrangeAsync(int maxDeliveries = 8)
    {
        await AuthenticateAsync();
        _apiKey = (await ReadAsync<MerchantWithKeyDto>(await Client.PostAsJsonAsync("/api/merchants", new MerchantRequest("Möbler AB")))).ApiKey;
        Client.DefaultRequestHeaders.Authorization = null;

        return await DbAsync(async db =>
        {
            var zone = new Zone("City", [new PostcodeRange(11100, 11899)]);
            var truck = new Truck("Bil 1", "ABC123", 20m, 1000m);
            var slot = new Slot(zone.Id, DeliveryDay, new TimeOnly(8, 0), new TimeOnly(12, 0), maxDeliveries);
            db.AddRange(zone, truck, new ZoneDayTruck(zone.Id, DeliveryDay, truck.Id), slot);
            await db.SaveChangesAsync();
            return slot;
        });
    }

    private static CreateOrderRequest OrderRequest(string reference = "WEB-1", string postcode = "114 55", int sofas = 1)
        => new(reference, new RecipientRequest("Anna Andersson", "+46701234567", "anna@example.com"),
            new AddressRequest("Storgatan 1", postcode, "Stockholm"), DeliveryType.Curbside,
            [new OrderLineRequest("SOFA-3", "3-sits soffa", sofas, 1.85m, 62.5m)]);

    private Task<HttpResponseMessage> PostOrderAsync(CreateOrderRequest order, string? apiKey = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/merchant/orders") { Content = JsonContent.Create(order, options: Json) };
        request.Headers.Add("X-Api-Key", apiKey ?? _apiKey);
        return Client.SendAsync(request);
    }

    private async Task<OrderDto> CreateOrderAsync(CreateOrderRequest? order = null)
        => await ReadAsync<OrderDto>(await PostOrderAsync(order ?? OrderRequest()));

    /// <summary>Calls a customer endpoint the way the app does: with the token from the link as a bearer token.</summary>
    private Task<HttpResponseMessage> AsCustomerAsync(HttpMethod method, string url, string link, object? body = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = body is null ? null : JsonContent.Create(body, options: Json) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", link.Split("/b/")[1]);
        return Client.SendAsync(request);
    }

    private Task<Guid> DeliveryIdAsync(long orderNumber) => DbAsync(db => (
        from o in db.Orders join d in db.Deliveries on o.Id equals d.OrderId where o.OrderNumber == orderNumber select d.Id).SingleAsync());

    [Fact]
    public async Task Sending_the_same_order_twice_gives_one_order()
    {
        await ArrangeAsync();

        var first = await PostOrderAsync(OrderRequest());
        var second = await PostOrderAsync(OrderRequest());

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var (a, b) = (await ReadAsync<OrderDto>(first), await ReadAsync<OrderDto>(second));
        Assert.Equal(a.OrderNumber, b.OrderNumber);
        Assert.Equal((DeliveryStatus.AwaitingBooking, GeocodeStatus.Ok), (a.Status, a.GeocodeStatus));
        Assert.StartsWith("https://book.example.test/b/", a.CustomerLink);
        Assert.Equal((1, 1), await DbAsync(async db => (await db.Orders.CountAsync(), await db.Deliveries.CountAsync())));

        var fetched = await Client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/merchant/orders/WEB-1") { Headers = { { "X-Api-Key", _apiKey } } });
        Assert.Equal(a.OrderNumber, (await ReadAsync<OrderDto>(fetched)).OrderNumber);
    }

    [Fact]
    public async Task The_same_reference_with_different_content_is_refused()
    {
        await ArrangeAsync();
        await CreateOrderAsync(OrderRequest(sofas: 1));

        Assert.Equal("OrderRefReused", await ErrorCodeAsync(await PostOrderAsync(OrderRequest(sofas: 2)), 409));
        Assert.Equal(1, await DbAsync(db => db.Orders.CountAsync()));
    }

    [Fact]
    public async Task The_same_order_sent_eight_times_at_once_still_gives_one_order()
    {
        await ArrangeAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => PostOrderAsync(OrderRequest()))));

        var orders = new List<OrderDto>();
        foreach (var response in responses)
            orders.Add(await ReadAsync<OrderDto>(response));
        Assert.Single(orders.Select(o => o.OrderNumber).Distinct());
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Equal((1, 1), await DbAsync(async db => (await db.Orders.CountAsync(), await db.Deliveries.CountAsync())));
    }

    [Fact]
    public async Task A_missing_wrong_or_replaced_api_key_is_refused()
    {
        await ArrangeAsync();

        Assert.Equal("InvalidApiKey", await ErrorCodeAsync(await Client.PostAsJsonAsync("/api/merchant/orders", OrderRequest(), Json), 401));
        Assert.Equal("InvalidApiKey", await ErrorCodeAsync(await PostOrderAsync(OrderRequest(), "pk_wrong"), 401));

        await AuthenticateAsync();
        var merchant = (await ReadAsync<MerchantDto[]>(await Client.GetAsync("/api/merchants"))).Single();
        var replaced = await ReadAsync<MerchantWithKeyDto>(await Client.PostAsync($"/api/merchants/{merchant.Id}/api-key", null));
        Client.DefaultRequestHeaders.Authorization = null;

        Assert.Equal("InvalidApiKey", await ErrorCodeAsync(await PostOrderAsync(OrderRequest()), 401));
        Assert.Equal(HttpStatusCode.Created, (await PostOrderAsync(OrderRequest(), replaced.ApiKey)).StatusCode);
    }

    [Fact]
    public async Task Bad_order_input_is_refused_with_a_code()
    {
        await ArrangeAsync();

        Assert.Equal("InvalidPostcode", await ErrorCodeAsync(await PostOrderAsync(OrderRequest(postcode: "1234")), 400));
        Assert.Equal("NoLines", await ErrorCodeAsync(await PostOrderAsync(OrderRequest() with { Lines = [] }), 400));
        Assert.Equal("Required", await ErrorCodeAsync(await PostOrderAsync(OrderRequest() with { Recipient = null! }), 400));
    }

    [Fact]
    public async Task When_the_geocoder_is_down_the_order_is_still_created_and_flagged()
    {
        await ArrangeAsync();
        Geocoder.IsDown = true;

        var order = await CreateOrderAsync();

        Assert.Equal(GeocodeStatus.Failed, order.GeocodeStatus);
        // A technical failure is not remembered, so the address gets another chance later.
        Assert.Equal(0, await DbAsync(db => db.GeocodeCache.CountAsync()));
    }

    [Fact]
    public async Task An_address_is_looked_up_once_and_then_answered_from_the_cache()
    {
        await ArrangeAsync();

        var first = await CreateOrderAsync(OrderRequest("WEB-1"));
        var second = await CreateOrderAsync(OrderRequest("WEB-2"));

        Assert.Equal(1, Geocoder.Calls);
        Assert.Equal((GeocodeStatus.Ok, GeocodeStatus.Ok), (first.GeocodeStatus, second.GeocodeStatus));
        var address = await DbAsync(db => db.Orders.AsNoTracking().Where(o => o.MerchantOrderRef == "WEB-2").Select(o => o.Address).SingleAsync());
        Assert.Equal((59.3293, 18.0686), (address.Lat, address.Lng));
    }

    [Fact]
    public async Task An_address_that_does_not_exist_is_flagged_and_that_answer_is_cached_too()
    {
        await ArrangeAsync();
        Geocoder.Answer = null;

        var first = await CreateOrderAsync(OrderRequest("WEB-1"));
        var second = await CreateOrderAsync(OrderRequest("WEB-2"));

        Assert.Equal((GeocodeStatus.Failed, GeocodeStatus.Failed), (first.GeocodeStatus, second.GeocodeStatus));
        Assert.Equal(1, Geocoder.Calls);
    }

    [Fact]
    public async Task The_customer_opens_the_link_sees_the_delivery_and_books_a_time()
    {
        var slot = await ArrangeAsync();
        var order = await CreateOrderAsync();

        var delivery = await ReadAsync<CustomerDeliveryDto>(await AsCustomerAsync(HttpMethod.Get, "/api/customer/delivery", order.CustomerLink));
        Assert.Equal((order.OrderNumber, DeliveryStatus.AwaitingBooking, "114 55", true), (delivery.OrderNumber, delivery.Status, delivery.Postcode, delivery.CanChangeSlot));
        Assert.Null(delivery.Slot);
        Assert.Equal([new CustomerLineDto("3-sits soffa", 1)], delivery.Lines);

        var slots = await ReadAsync<AvailableSlot[]>(await AsCustomerAsync(HttpMethod.Get, "/api/customer/slots", order.CustomerLink));
        Assert.Equal([slot.Id], slots.Select(s => s.Id));

        var booked = await ReadAsync<CustomerDeliveryDto>(
            await AsCustomerAsync(HttpMethod.Put, "/api/customer/delivery/slot", order.CustomerLink, new BookSlotRequest(slot.Id)));
        Assert.Equal((DeliveryStatus.Booked, slot.Id, DeliveryDay), (booked.Status, booked.Slot!.Id, booked.Slot.Date));
        // The link now lives until 7 days after the booked date.
        Assert.Equal(DeliveryDay.AddDays(7), booked.LinkValidThrough);

        var events = await DbAsync(db => db.DeliveryEvents.ToListAsync());
        Assert.Equal(ActorType.Customer, Assert.Single(events).ActorType);
    }

    [Fact]
    public async Task A_customer_outside_every_zone_gets_an_empty_list_not_an_error()
    {
        await ArrangeAsync();
        var order = await CreateOrderAsync(OrderRequest(postcode: "931 31"));

        var slots = await ReadAsync<AvailableSlot[]>(await AsCustomerAsync(HttpMethod.Get, "/api/customer/slots", order.CustomerLink));

        Assert.Empty(slots);
    }

    [Fact]
    public async Task Booking_a_time_that_just_filled_up_answers_409_SlotFull()
    {
        var slot = await ArrangeAsync(maxDeliveries: 1);
        var first = await CreateOrderAsync(OrderRequest("WEB-1"));
        var second = await CreateOrderAsync(OrderRequest("WEB-2"));

        await ReadAsync<CustomerDeliveryDto>(await AsCustomerAsync(HttpMethod.Put, "/api/customer/delivery/slot", first.CustomerLink, new BookSlotRequest(slot.Id)));
        var late = await AsCustomerAsync(HttpMethod.Put, "/api/customer/delivery/slot", second.CustomerLink, new BookSlotRequest(slot.Id));

        Assert.Equal(BookingErrors.SlotFull, await ErrorCodeAsync(late, 409));
        Assert.Empty(await ReadAsync<AvailableSlot[]>(await AsCustomerAsync(HttpMethod.Get, "/api/customer/slots", second.CustomerLink)));
    }

    [Fact]
    public async Task A_replaced_link_stops_working_and_the_new_one_works()
    {
        await ArrangeAsync();
        var order = await CreateOrderAsync();
        var deliveryId = await DeliveryIdAsync(order.OrderNumber);

        await AuthenticateAsync();
        var replacement = await ReadAsync<CustomerLinkDto>(await Client.PostAsync($"/api/deliveries/{deliveryId}/link", null));
        Client.DefaultRequestHeaders.Authorization = null;

        Assert.Equal("LinkRevoked", await ErrorCodeAsync(await AsCustomerAsync(HttpMethod.Get, "/api/customer/delivery", order.CustomerLink), 401));
        Assert.Equal("LinkRevoked", await ErrorCodeAsync(await AsCustomerAsync(HttpMethod.Get, "/api/customer/slots", order.CustomerLink), 401));
        Assert.Equal(HttpStatusCode.OK, (await AsCustomerAsync(HttpMethod.Get, "/api/customer/delivery", replacement.CustomerLink)).StatusCode);
    }

    [Theory]
    [InlineData(7, true)]
    [InlineData(8, false)]
    public async Task A_link_works_through_the_seventh_day_after_the_delivery_date_and_not_after(int daysAgo, bool works)
    {
        await ArrangeAsync();
        var order = await CreateOrderAsync();
        var deliveryId = await DeliveryIdAsync(order.OrderNumber);
        await DbAsync(async db =>
        {
            var zoneId = await db.Zones.Select(z => z.Id).SingleAsync();
            var past = new Slot(zoneId, Today.AddDays(-daysAgo), new TimeOnly(8, 0), new TimeOnly(12, 0), 8);
            db.Slots.Add(past);
            (await db.Deliveries.SingleAsync(d => d.Id == deliveryId)).Book(past.Id, Actor.System, DateTimeOffset.UtcNow.AddDays(-daysAgo - 2));
            return await db.SaveChangesAsync();
        });

        var response = await AsCustomerAsync(HttpMethod.Get, "/api/customer/delivery", order.CustomerLink);

        if (works)
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        else
            Assert.Equal("LinkExpired", await ErrorCodeAsync(response, 401));
    }

    [Fact]
    public async Task A_link_for_an_order_nobody_booked_expires_seven_days_after_the_order_came_in()
    {
        await ArrangeAsync();
        var order = await CreateOrderAsync();
        await DbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE Orders SET CreatedAt = DATEADD(day, -8, CreatedAt)"));

        var response = await AsCustomerAsync(HttpMethod.Get, "/api/customer/delivery", order.CustomerLink);

        Assert.Equal("LinkExpired", await ErrorCodeAsync(response, 401));
    }

    [Fact]
    public async Task Customer_and_dispatcher_tokens_are_not_interchangeable()
    {
        await ArrangeAsync();
        var order = await CreateOrderAsync();

        // Changing one character breaks the signature.
        var tampered = order.CustomerLink[..^3] + (order.CustomerLink.EndsWith("AAA") ? "BBB" : "AAA");
        Assert.Equal(HttpStatusCode.Unauthorized, (await AsCustomerAsync(HttpMethod.Get, "/api/customer/delivery", tampered)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.GetAsync("/api/customer/delivery")).StatusCode);

        // A customer token on a dispatcher endpoint.
        Assert.Equal(HttpStatusCode.Unauthorized, (await AsCustomerAsync(HttpMethod.Get, "/api/trucks", order.CustomerLink)).StatusCode);

        // A dispatcher token on a customer endpoint.
        await AuthenticateAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.GetAsync("/api/customer/delivery")).StatusCode);
    }
}
