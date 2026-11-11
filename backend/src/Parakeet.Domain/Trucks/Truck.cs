using Parakeet.Domain.Common;

namespace Parakeet.Domain.Trucks;

public class Truck
{
    private Truck() { }

    public Truck(string name, string plate, decimal capacityM3, decimal capacityKg)
    {
        Id = Guid.CreateVersion7();
        Update(name, plate, capacityM3, capacityKg);
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public string Plate { get; private set; } = null!;
    public decimal CapacityM3 { get; private set; }
    public decimal CapacityKg { get; private set; }
    public bool IsActive { get; private set; } = true;

    public void Update(string name, string plate, decimal capacityM3, decimal capacityKg)
    {
        Name = Guard.Required(name, nameof(Name), 100);
        Plate = Guard.Required(plate, nameof(Plate), 16);
        CapacityM3 = Guard.Positive(capacityM3, nameof(CapacityM3));
        CapacityKg = Guard.Positive(capacityKg, nameof(CapacityKg));
    }

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
}
