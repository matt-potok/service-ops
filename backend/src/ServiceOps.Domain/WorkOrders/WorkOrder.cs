using ServiceOps.Domain.Customers;

namespace ServiceOps.Domain.WorkOrders;

public sealed class WorkOrder
{
    private readonly List<WorkOrderActivity> activities = [];
    private WorkOrder() { }

    public Guid Id { get; private set; }
    public string Number { get; private set; } = ""; // Assigned by the database sequence on insert.
    public Guid LocationId { get; private set; }
    public Location Location { get; private set; } = null!;
    public string Title { get; private set; } = "";
    public string Description { get; private set; } = "";
    public ServiceType ServiceType { get; private set; }
    public Priority Priority { get; private set; }
    public WorkOrderStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public int SlaPolicyVersion { get; private set; }
    public int SlaDurationMinutes { get; private set; }
    public DateTimeOffset SlaAtRiskAt { get; private set; }
    public DateTimeOffset SlaDeadlineAt { get; private set; }
    public int Revision { get; private set; }
    public int SlaRevision { get; private set; }
    public IReadOnlyCollection<WorkOrderActivity> Activities => activities.AsReadOnly();

    public static WorkOrder Create(Customer customer, Location location, ServiceType serviceType,
        Priority priority, string title, string description, Guid createdByUserId, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (!customer.IsActive) throw new ArgumentException("Choose an active customer.", "customerId");
        if (!location.IsActive || location.CustomerId != customer.Id)
            throw new ArgumentException("Choose an active location belonging to this customer.", "locationId");
        if (!Enum.IsDefined(serviceType)) throw new ArgumentException("Choose a valid service type.", "serviceType");
        if (createdByUserId == Guid.Empty) throw new ArgumentException("A creator is required.", nameof(createdByUserId));
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 200)
            throw new ArgumentException("Enter a title of 1–200 characters.", nameof(title));
        if (string.IsNullOrWhiteSpace(description) || description.Trim().Length > 10_000)
            throw new ArgumentException("Enter a description of 1–10,000 characters.", nameof(description));

        var duration = SlaDurationFor(priority);
        var createdAt = timeProvider.GetUtcNow();
        var order = new WorkOrder
        {
            Id = Guid.NewGuid(), LocationId = location.Id, Location = location,
            ServiceType = serviceType, Priority = priority, Title = title.Trim(), Description = description.Trim(),
            Status = WorkOrderStatus.New, CreatedAt = createdAt, CreatedByUserId = createdByUserId,
            SlaPolicyVersion = 1, SlaDurationMinutes = duration,
            SlaAtRiskAt = createdAt.AddMinutes(duration * 0.75), SlaDeadlineAt = createdAt.AddMinutes(duration),
            Revision = 1, SlaRevision = 1
        };
        order.activities.Add(new WorkOrderActivity(order.Id, createdByUserId, createdAt));
        return order;
    }

    public static int SlaDurationFor(Priority priority) => priority switch
    {
        Priority.Critical => 120,
        Priority.High => 240,
        Priority.Normal => 480,
        Priority.Low => 1440,
        _ => throw new ArgumentException("Choose a valid priority.", nameof(priority))
    };

    // The caller captures one current server instant for the response. No stored SLA status or worker is needed.
    public SlaState GetSlaState(DateTimeOffset evaluatedAt) => evaluatedAt >= SlaDeadlineAt
        ? SlaState.Breached : evaluatedAt >= SlaAtRiskAt ? SlaState.AtRisk : SlaState.Good;
}
