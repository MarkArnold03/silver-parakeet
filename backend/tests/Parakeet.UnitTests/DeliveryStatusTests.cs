using Parakeet.Domain.Common;
using Parakeet.Domain.Deliveries;
using static Parakeet.Domain.Deliveries.DeliveryStatus;

namespace Parakeet.UnitTests;

public class DeliveryStatusTests
{
    private static readonly DateTimeOffset Now = TestData.Now;

    [Fact]
    public void New_delivery_awaits_booking()
    {
        var d = TestData.Delivery();
        Assert.Equal(AwaitingBooking, d.Status);
        Assert.Null(d.SlotId);
        Assert.Equal(1, d.LinkVersion);
    }

    [Fact]
    public void Happy_path_goes_all_the_way_to_delivered_and_logs_every_step()
    {
        var d = TestData.DeliveryIn(Delivered);

        Assert.Equal(Delivered, d.Status);
        Assert.Equal(
            [Booked, Planned, Loaded, OutForDelivery, Delivered],
            d.Events.Select(e => e.ToStatus!.Value));
        Assert.Equal(DeliveryEventType.StatusChanged, d.Events.First().Type);
        Assert.Equal(ActorType.Customer, d.Events.First().ActorType);
        Assert.Equal("user-1", d.Events.Last().ActorId);
    }

    // Every pair the status endpoint must accept. Everything else must be refused.
    public static readonly (DeliveryStatus, DeliveryStatus)[] Allowed =
        [(Planned, Loaded), (Loaded, OutForDelivery), (OutForDelivery, Delivered), (OutForDelivery, Failed)];

    public static TheoryData<DeliveryStatus, DeliveryStatus> AllPairs()
    {
        var data = new TheoryData<DeliveryStatus, DeliveryStatus>();
        foreach (var from in Enum.GetValues<DeliveryStatus>())
            foreach (var to in Enum.GetValues<DeliveryStatus>())
                data.Add(from, to);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void Status_endpoint_allows_only_the_four_forward_steps(DeliveryStatus from, DeliveryStatus to)
    {
        var d = TestData.DeliveryIn(from);
        var act = () => d.ChangeStatus(to, TestData.Dispatcher, "reason", Now);

        if (Allowed.Contains((from, to)))
        {
            act();
            Assert.Equal(to, d.Status);
        }
        else
        {
            var ex = Assert.ThrowsAny<DomainException>(act);
            Assert.Equal("IllegalTransition", ex.Code);
            Assert.Equal(from, d.Status);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Failed_needs_a_reason(string? reason)
    {
        var d = TestData.DeliveryIn(OutForDelivery);
        Assert.ThrowsAny<DomainException>(() => d.ChangeStatus(Failed, TestData.Dispatcher, reason, Now));
        Assert.Equal(OutForDelivery, d.Status);
    }

    [Fact]
    public void Failed_leaves_the_route_keeps_the_slot_and_stores_the_reason()
    {
        var d = TestData.DeliveryIn(OutForDelivery);
        var slot = d.SlotId;

        d.ChangeStatus(Failed, TestData.Dispatcher, "Nobody home", Now);

        Assert.Null(d.RouteId);
        Assert.Null(d.StopSequence);
        Assert.Equal(slot, d.SlotId);
        Assert.Equal("Nobody home", d.FailureReason);
        Assert.Equal("Nobody home", d.Events.Last().Reason);
    }

    [Theory]
    [InlineData(AwaitingBooking, true)]
    [InlineData(Booked, true)]
    [InlineData(Planned, true)]
    [InlineData(Failed, true)]
    [InlineData(Loaded, false)]
    [InlineData(OutForDelivery, false)]
    [InlineData(Delivered, false)]
    public void Booking_is_allowed_only_before_loading_or_after_a_failure(DeliveryStatus from, bool allowed)
    {
        var d = TestData.DeliveryIn(from);
        var newSlot = Guid.NewGuid();

        if (allowed)
        {
            d.Book(newSlot, Actor.Customer, Now);
            Assert.Equal(Booked, d.Status);
            Assert.Equal(newSlot, d.SlotId);
            Assert.Null(d.FailureReason);
        }
        else
        {
            var ex = Assert.ThrowsAny<DomainException>(() => d.Book(newSlot, Actor.Customer, Now));
            Assert.Equal("RebookNotAllowed", ex.Code);
        }
    }

    [Fact]
    public void Rebooking_a_planned_delivery_takes_it_off_the_route_first()
    {
        var d = TestData.DeliveryIn(Planned);

        d.Book(Guid.NewGuid(), Actor.Customer, Now);

        Assert.Equal(Booked, d.Status);
        Assert.Null(d.RouteId);
        Assert.Equal(
            [DeliveryEventType.Unassigned, DeliveryEventType.Rebooked],
            d.Events.TakeLast(2).Select(e => e.Type));
    }

    [Fact]
    public void Booking_the_same_slot_again_changes_nothing()
    {
        var d = TestData.DeliveryIn(Planned);
        var events = d.Events.Count;

        d.Book(d.SlotId!.Value, Actor.Customer, Now);

        Assert.Equal(Planned, d.Status);
        Assert.NotNull(d.RouteId);
        Assert.Equal(events, d.Events.Count);
    }

    [Fact]
    public void Only_cutoff_for_booked_and_planned_depends_on_the_current_slot()
    {
        Assert.True(DeliveryStatusRules.RebookNeedsCurrentSlotBeforeCutoff(Booked));
        Assert.True(DeliveryStatusRules.RebookNeedsCurrentSlotBeforeCutoff(Planned));
        Assert.False(DeliveryStatusRules.RebookNeedsCurrentSlotBeforeCutoff(AwaitingBooking));
        Assert.False(DeliveryStatusRules.RebookNeedsCurrentSlotBeforeCutoff(Failed));
    }

    [Theory]
    [InlineData(AwaitingBooking)]
    [InlineData(Loaded)]
    [InlineData(Delivered)]
    [InlineData(Failed)]
    public void Assign_needs_a_booked_or_planned_delivery(DeliveryStatus from)
    {
        var d = TestData.DeliveryIn(from);
        Assert.ThrowsAny<DomainException>(() => d.AssignToRoute(Guid.NewGuid(), 1, TestData.Dispatcher, Now));
    }

    [Fact]
    public void Assign_and_unassign_move_between_booked_and_planned()
    {
        var d = TestData.DeliveryIn(Booked);
        var route = Guid.NewGuid();

        d.AssignToRoute(route, 3, TestData.Dispatcher, Now);
        Assert.Equal((Planned, route, 3), (d.Status, d.RouteId!.Value, d.StopSequence!.Value));

        d.Unassign(TestData.Dispatcher, "Truck broke down", Now);
        Assert.Equal(Booked, d.Status);
        Assert.Null(d.RouteId);
        Assert.Equal("Truck broke down", d.Events.Last().Reason);

        Assert.ThrowsAny<DomainException>(() => d.Unassign(TestData.Dispatcher, null, Now));
    }

    [Fact]
    public void Revoking_links_bumps_the_link_version()
    {
        var d = TestData.Delivery();
        d.RevokeLinks();
        Assert.Equal(2, d.LinkVersion);
    }
}
