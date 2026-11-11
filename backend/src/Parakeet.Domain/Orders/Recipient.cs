using Parakeet.Domain.Common;

namespace Parakeet.Domain.Orders;

public class Recipient
{
    private Recipient() { }

    public Recipient(string name, string phone, string? email)
    {
        Name = Guard.Required(name, nameof(Name), 200);
        Phone = Guard.Required(phone, nameof(Phone), 32);
        Email = Guard.Optional(email, nameof(Email), 254);
    }

    public string Name { get; private set; } = null!;
    public string Phone { get; private set; } = null!;
    public string? Email { get; private set; }
}
