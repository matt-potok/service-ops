using ServiceOps.Domain.Customers;
using ServiceOps.Domain.Technicians;
using System.Text.Json;

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
    public Guid? TechnicianId { get; private set; }
    public Technician? Technician { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public string? HoldReason { get; private set; }
    public string? ResolutionSummary { get; private set; }
    public string? CancellationReason { get; private set; }
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
            Status = WorkOrderStatus.New, CreatedAt = createdAt, UpdatedAt = createdAt, CreatedByUserId = createdByUserId,
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
    public SlaState? GetSlaState(DateTimeOffset evaluatedAt) => Status is WorkOrderStatus.Completed or WorkOrderStatus.Cancelled ? null : evaluatedAt >= SlaDeadlineAt
        ? SlaState.Breached : evaluatedAt >= SlaAtRiskAt ? SlaState.AtRisk : SlaState.Good;

    public void Assign(Technician technician, Guid actor, TimeProvider clock)
    {
        RequireOpen(actor);
        ArgumentNullException.ThrowIfNull(technician);
        if (!technician.IsActive) throw new ArgumentException("Choose an active technician.", "technicianId");
        if (TechnicianId == technician.Id) return;
        var previous = TechnicianId;
        var previousName = Technician?.DisplayName;
        var previousStatus = Status;
        TechnicianId = technician.Id;
        Technician = technician;
        if (Status == WorkOrderStatus.New) Status = WorkOrderStatus.Assigned;
        Record(previous.HasValue ? WorkOrderEventType.Reassigned : WorkOrderEventType.Assigned, actor, clock,
            new { previousTechnicianId = previous, previousTechnicianName = previousName,
                technicianId = technician.Id, technicianName = technician.DisplayName,
                previousStatus = previousStatus.ToString(), status = Status.ToString() });
    }

    public void Unassign(Guid actor, TimeProvider clock)
    {
        RequireOpen(actor);
        if (Status == WorkOrderStatus.New) return;
        RequireStatus(WorkOrderStatus.Assigned);
        var previous = TechnicianId;
        var previousName = Technician?.DisplayName;
        TechnicianId = null;
        Technician = null;
        Status = WorkOrderStatus.New;
        Record(WorkOrderEventType.Unassigned, actor, clock, new { previousTechnicianId = previous,
            previousTechnicianName = previousName, previousStatus = "Assigned", status = "New" });
    }

    public void UpdateDetails(string title, string description, Guid actor, TimeProvider clock)
    {
        RequireOpen(actor);
        title = RequiredText(title, 200, nameof(title));
        description = RequiredText(description, 10000, nameof(description));
        // Store only fields that changed, not a duplicate snapshot of the order.
        var changes = new Dictionary<string, object>();
        if (Title != title) changes.Add("title", new { before = Title, after = title });
        if (Description != description) changes.Add("description", new { before = Description, after = description });
        if (changes.Count == 0) return;
        Title = title;
        Description = description;
        Record(WorkOrderEventType.DetailsCorrected, actor, clock, changes);
    }

    public void StartWork(Guid actor, TimeProvider clock)
    {
        RequireOpen(actor);
        RequireStatus(WorkOrderStatus.Assigned);
        Status = WorkOrderStatus.InProgress;
        Record(WorkOrderEventType.Started, actor, clock, new { previousStatus = "Assigned", status = "InProgress" });
    }

    public void PlaceOnHold(string reason, Guid actor, TimeProvider clock)
    {
        RequireOpen(actor);
        RequireStatus(WorkOrderStatus.InProgress);
        HoldReason = RequiredText(reason, 5000, nameof(reason));
        Status = WorkOrderStatus.OnHold;
        Record(WorkOrderEventType.PlacedOnHold, actor, clock,
            new { previousStatus = "InProgress", status = "OnHold", reason = HoldReason });
    }

    public void Resume(Guid actor, TimeProvider clock)
    {
        RequireOpen(actor);
        RequireStatus(WorkOrderStatus.OnHold);
        Status = WorkOrderStatus.InProgress;
        HoldReason = null;
        Record(WorkOrderEventType.Resumed, actor, clock, new { previousStatus = "OnHold", status = "InProgress" });
    }

    public void Complete(string summary, Guid actor, TimeProvider clock)
    {
        RequireOpen(actor);
        if (Status is not (WorkOrderStatus.InProgress or WorkOrderStatus.OnHold) || TechnicianId is null)
            throw new InvalidOperationException("Only work that has started with an assigned technician can be completed.");
        ResolutionSummary = RequiredText(summary, 5000, nameof(summary));
        var previous = Status;
        Status = WorkOrderStatus.Completed;
        HoldReason = null;
        Record(WorkOrderEventType.Completed, actor, clock,
            new { previousStatus = previous.ToString(), status = "Completed", summary = ResolutionSummary });
        CompletedAt = UpdatedAt;
    }

    public void Cancel(string reason, Guid actor, TimeProvider clock)
    {
        RequireOpen(actor);
        CancellationReason = RequiredText(reason, 5000, nameof(reason));
        var previous = Status;
        Status = WorkOrderStatus.Cancelled;
        HoldReason = null;
        Record(WorkOrderEventType.Cancelled, actor, clock,
            new { previousStatus = previous.ToString(), status = "Cancelled", reason = CancellationReason });
        CancelledAt = UpdatedAt;
    }

    private void RequireOpen(Guid actor)
    {
        if (actor == Guid.Empty) throw new ArgumentException("An actor is required.", nameof(actor));
        if (Status is WorkOrderStatus.Completed or WorkOrderStatus.Cancelled)
            throw new InvalidOperationException("Completed and cancelled work orders cannot be changed.");
    }

    private void RequireStatus(WorkOrderStatus required)
    {
        if (Status != required) throw new InvalidOperationException($"This action requires {required} status. Reload and review the work order.");
    }

    private static string RequiredText(string value, int maximum, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > maximum)
            throw new ArgumentException($"Enter 1–{maximum:N0} characters.", field);
        return value.Trim();
    }

    private void Record(WorkOrderEventType type, Guid actor, TimeProvider clock, object changes)
    {
        UpdatedAt = clock.GetUtcNow();
        Revision++;
        activities.Add(new WorkOrderActivity(Id, actor, UpdatedAt, type, JsonSerializer.Serialize(changes)));
    }
}
