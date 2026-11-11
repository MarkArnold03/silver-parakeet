using Parakeet.Domain.Common;

namespace Parakeet.Domain.Deliveries;

/// <summary>Someone else who may receive the delivery, chosen by the customer.</summary>
public class AlternativeRecipient
{
    private AlternativeRecipient() { }

    public AlternativeRecipient(string name, string phone)
    {
        Name = Guard.Required(name, nameof(Name), 200);
        Phone = Guard.Required(phone, nameof(Phone), 32);
    }

    public string Name { get; private set; } = null!;
    public string Phone { get; private set; } = null!;
}
