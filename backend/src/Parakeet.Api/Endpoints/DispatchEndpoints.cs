using Parakeet.Application.Slots;
using Parakeet.Application.Trucks;
using Parakeet.Application.Zones;

namespace Parakeet.Api.Endpoints;

/// <summary>Everything here needs a logged-in dispatcher.</summary>
public static class DispatchEndpoints
{
    public static void MapDispatchEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();

        var trucks = api.MapGroup("/trucks").WithTags("Trucks");
        trucks.MapGet("", (TruckService s, CancellationToken ct) => s.ListAsync(ct));
        trucks.MapGet("{id:guid}", (Guid id, TruckService s, CancellationToken ct) => s.GetAsync(id, ct));
        trucks.MapPost("", async (TruckRequest request, TruckService s, CancellationToken ct) =>
        {
            var truck = await s.CreateAsync(request, ct);
            return Results.Created($"/api/trucks/{truck.Id}", truck);
        }).Produces<TruckDto>(201);
        trucks.MapPut("{id:guid}", (Guid id, TruckRequest request, TruckService s, CancellationToken ct) => s.UpdateAsync(id, request, ct));

        var zones = api.MapGroup("/zones").WithTags("Zones");
        zones.MapGet("", (ZoneService s, CancellationToken ct) => s.ListAsync(ct));
        zones.MapGet("{id:guid}", (Guid id, ZoneService s, CancellationToken ct) => s.GetAsync(id, ct));
        zones.MapPost("", async (ZoneRequest request, ZoneService s, CancellationToken ct) =>
        {
            var zone = await s.CreateAsync(request, ct);
            return Results.Created($"/api/zones/{zone.Id}", zone);
        }).Produces<ZoneDto>(201);
        zones.MapPut("{id:guid}", (Guid id, ZoneRequest request, ZoneService s, CancellationToken ct) => s.UpdateAsync(id, request, ct));
        zones.MapDelete("{id:guid}", async (Guid id, ZoneService s, CancellationToken ct) =>
        {
            await s.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        var slots = api.MapGroup("/slots").WithTags("Slots");
        slots.MapGet("", (Guid zoneId, DateOnly from, DateOnly to, SlotService s, CancellationToken ct) => s.ListAsync(zoneId, from, to, ct));
        slots.MapPost("generate", (GenerateSlotsRequest request, SlotService s, CancellationToken ct) => s.GenerateAsync(request, ct));
        slots.MapPut("{id:guid}", (Guid id, UpdateSlotRequest request, SlotService s, CancellationToken ct) => s.UpdateAsync(id, request, ct));
        slots.MapDelete("{id:guid}", async (Guid id, SlotService s, CancellationToken ct) =>
        {
            await s.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        var zoneDays = api.MapGroup("/zone-days").WithTags("Zone days");
        zoneDays.MapGet("", (DateOnly from, DateOnly to, Guid? zoneId, SlotService s, CancellationToken ct) => s.ListZoneDaysAsync(zoneId, from, to, ct));
        zoneDays.MapPut("{zoneId:guid}/{date}/trucks", (Guid zoneId, DateOnly date, SetZoneDayTrucksRequest request, SlotService s, CancellationToken ct) =>
            s.SetZoneDayTrucksAsync(zoneId, date, request, ct));
    }
}
