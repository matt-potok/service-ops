namespace ServiceOps.Domain.Technicians;

public sealed class Technician
{
    private Technician() { }
    public Technician(Guid id, string displayName, string email)
    {
        if (id == Guid.Empty) throw new ArgumentException("Technician ID is required.", nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        Id = id; DisplayName = displayName.Trim(); Email = email.Trim();
    }

    public Guid Id { get; private set; }
    public string DisplayName { get; private set; } = "";
    public string Email { get; private set; } = "";
    public bool IsActive { get; private set; } = true;
}
