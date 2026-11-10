using Parakeet.Domain.Common;
using Parakeet.Domain.Deliveries;
using Parakeet.Domain.Orders;

namespace Parakeet.UnitTests;

public class OrderTests
{
    [Fact]
    public void Totals_are_summed_from_the_lines()
    {
        var order = TestData.Order(
            new OrderLine("SOFA", "Sofa", 1, 1.850m, 62.50m),
            new OrderLine("CHAIR", "Chair", 4, 0.125m, 4.25m),
            new OrderLine("LAMP", "Lamp", 2, 0.040m, 1.10m));

        Assert.Equal(1.850m + 0.500m + 0.080m, order.TotalVolumeM3);
        Assert.Equal(62.50m + 17.00m + 2.20m, order.TotalWeightKg);
    }

    [Fact]
    public void Delivery_copies_the_order_totals()
    {
        var order = TestData.Order(new OrderLine("CHAIR", "Chair", 4, 0.125m, 4.25m));
        var delivery = new Delivery(order);

        Assert.Equal(0.500m, delivery.VolumeM3);
        Assert.Equal(17.00m, delivery.WeightKg);
        Assert.Equal(order.Id, delivery.OrderId);
    }

    [Fact]
    public void An_order_needs_at_least_one_line()
    {
        var ex = Assert.Throws<DomainException>(() => new Order(
            Guid.NewGuid(), "REF", new Recipient("A", "1", null), new Address("S 1", "11455", "Sthlm"),
            DeliveryType.Doorstep, [], new string('a', 64), TestData.Now));
        Assert.Equal("NoLines", ex.Code);
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(-1, 1, 1)]
    [InlineData(1, -0.001, 1)]
    [InlineData(1, 1, -0.01)]
    public void Lines_reject_bad_numbers(int quantity, double volume, double weight)
        => Assert.Throws<DomainException>(() => new OrderLine("A", "B", quantity, (decimal)volume, (decimal)weight));

    [Fact]
    public void Required_text_is_trimmed_and_checked()
    {
        Assert.Equal("Anna", new Recipient("  Anna ", "070", null).Name);
        Assert.Null(new Recipient("Anna", "070", "  ").Email);
        Assert.Throws<DomainException>(() => new Recipient(" ", "070", null));
    }
}
