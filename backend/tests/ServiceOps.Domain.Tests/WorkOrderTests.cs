using ServiceOps.Domain.Customers;
using ServiceOps.Domain.WorkOrders;
using Xunit;

namespace ServiceOps.Domain.Tests;

public sealed class WorkOrderTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 9, 29, 8, 0, 0, TimeSpan.Zero);
    private readonly Customer customer = new(Guid.NewGuid(), "CEDAR", "Cedar Vale Offices");

    [Theory]
    [InlineData(Priority.Critical, 120, 90)]
    [InlineData(Priority.High, 240, 180)]
    [InlineData(Priority.Normal, 480, 360)]
    [InlineData(Priority.Low, 1440, 1080)]
    public void Creation_sets_duration_timestamps_and_exact_sla_boundaries(Priority priority, int duration, int risk)
    {
        var actor = Guid.NewGuid();
        var order = WorkOrder.Create(customer, Location(), ServiceType.HVAC, priority, " Cooling failure ", " East wing is warm. ", actor, new FixedTime());
        Assert.Equal(CreatedAt, order.CreatedAt);
        Assert.Equal(duration, order.SlaDurationMinutes);
        Assert.Equal(CreatedAt.AddMinutes(risk), order.SlaAtRiskAt);
        Assert.Equal(CreatedAt.AddMinutes(duration), order.SlaDeadlineAt);
        Assert.Equal(SlaState.Good, order.GetSlaState(order.SlaAtRiskAt.AddTicks(-1)));
        Assert.Equal(SlaState.AtRisk, order.GetSlaState(order.SlaAtRiskAt));
        Assert.Equal(SlaState.AtRisk, order.GetSlaState(order.SlaDeadlineAt.AddTicks(-1)));
        Assert.Equal(SlaState.Breached, order.GetSlaState(order.SlaDeadlineAt));
        Assert.Equal("Cooling failure", order.Title);
        Assert.Equal("East wing is warm.", order.Description);
        Assert.Equal(WorkOrderStatus.New, order.Status);
        Assert.Equal(1, order.Revision);
        Assert.Equal(1, order.SlaRevision);
        var activity = Assert.Single(order.Activities);
        Assert.Equal(order.Id, activity.WorkOrderId);
        Assert.Equal(actor, activity.ActorUserId);
        Assert.Equal(WorkOrderEventType.Created, activity.EventType);
        Assert.Equal(CreatedAt, activity.EffectiveAt);
        Assert.Equal(CreatedAt, activity.RecordedAt);
    }

    [Theory]
    [InlineData("", "Description")]
    [InlineData("   ", "Description")]
    [InlineData("Title", " ")]
    public void Creation_requires_meaningful_text(string title, string description) =>
        Assert.Throws<ArgumentException>(() => WorkOrder.Create(customer, Location(), ServiceType.HVAC, Priority.Normal,
            title, description, Guid.NewGuid(), new FixedTime()));

    [Fact]
    public void Creation_rejects_mismatched_customer_and_location()
    {
        var other = new Customer(Guid.NewGuid(), "HARBOR", "Harborstone Logistics");
        Assert.Throws<ArgumentException>(() => WorkOrder.Create(other, Location(), ServiceType.HVAC, Priority.High,
            "Cooling failure", "East wing is warm.", Guid.NewGuid(), new FixedTime()));
    }

    [Fact]
    public void Creation_rejects_undefined_classification_and_missing_actor()
    {
        Assert.Throws<ArgumentException>(() => WorkOrder.SlaDurationFor((Priority)99));
        Assert.Throws<ArgumentException>(() => WorkOrder.Create(customer, Location(), (ServiceType)99, Priority.Normal,
            "Cooling failure", "East wing is warm.", Guid.NewGuid(), new FixedTime()));
        Assert.Throws<ArgumentException>(() => WorkOrder.Create(customer, Location(), ServiceType.HVAC, Priority.Normal,
            "Cooling failure", "East wing is warm.", Guid.Empty, new FixedTime()));
    }

    private Location Location() => new(Guid.NewGuid(), customer.Id, "WILLOW", "Willow Park Campus", "120 Park Avenue, Princeton, NJ");
    private sealed class FixedTime : TimeProvider { public override DateTimeOffset GetUtcNow() => CreatedAt; }
}
