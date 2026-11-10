using Parakeet.Domain.Common;

namespace Parakeet.Domain.Orders;

public class OrderLine
{
    private OrderLine() { }

    public OrderLine(string articleNumber, string description, int quantity, decimal unitVolumeM3, decimal unitWeightKg)
    {
        Id = Guid.CreateVersion7();
        ArticleNumber = Guard.Required(articleNumber, nameof(ArticleNumber), 64);
        Description = Guard.Required(description, nameof(Description), 300);
        Quantity = quantity > 0 ? quantity : throw new DomainException("NotPositive", "Quantity must be at least 1.");
        UnitVolumeM3 = Guard.NotNegative(unitVolumeM3, nameof(UnitVolumeM3));
        UnitWeightKg = Guard.NotNegative(unitWeightKg, nameof(UnitWeightKg));
    }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public string ArticleNumber { get; private set; } = null!;
    public string Description { get; private set; } = null!;
    public int Quantity { get; private set; }
    public decimal UnitVolumeM3 { get; private set; }
    public decimal UnitWeightKg { get; private set; }

    public decimal VolumeM3 => Quantity * UnitVolumeM3;
    public decimal WeightKg => Quantity * UnitWeightKg;
}
