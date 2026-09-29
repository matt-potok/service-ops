namespace ServiceOps.Domain.Customers;

public sealed class Location
{
    private Location() { }
    public Location(Guid id, Guid customerId, string code, string name, string address)
    {
        if (id == Guid.Empty || customerId == Guid.Empty) throw new ArgumentException("Location and customer IDs are required.");
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        Id = id; CustomerId = customerId; Code = code.Trim(); Name = name.Trim(); Address = address.Trim();
    }

    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public Customer Customer { get; private set; } = null!;
    public string Code { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string Address { get; private set; } = "";
    public bool IsActive { get; private set; } = true;
}
