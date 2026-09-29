namespace ServiceOps.Domain.Customers;

public sealed class Customer
{
    private Customer() { }
    public Customer(Guid id, string code, string name)
    {
        if (id == Guid.Empty) throw new ArgumentException("Customer ID is required.", nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Id = id; Code = code.Trim(); Name = name.Trim();
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = "";
    public string Name { get; private set; } = "";
    public bool IsActive { get; private set; } = true;
}
