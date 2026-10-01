namespace ServiceOps.Domain.WorkOrders;

public sealed class WorkOrderActivity
{
    private WorkOrderActivity() { }
    internal WorkOrderActivity(Guid workOrderId, Guid actorUserId, DateTimeOffset createdAt,
        WorkOrderEventType eventType = WorkOrderEventType.Created, string? changes = null)
    {
        Id = Guid.NewGuid();
        WorkOrderId = workOrderId;
        ActorUserId = actorUserId;
        EventType = eventType;
        Changes = changes;
        EffectiveAt = createdAt;
        RecordedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid WorkOrderId { get; private set; }
    public Guid ActorUserId { get; private set; }
    public WorkOrderEventType EventType { get; private set; }
    public DateTimeOffset EffectiveAt { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
    public string? Changes { get; private set; }
}
