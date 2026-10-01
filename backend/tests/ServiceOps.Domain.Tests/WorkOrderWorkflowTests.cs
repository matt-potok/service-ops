using ServiceOps.Domain.Customers;
using ServiceOps.Domain.Technicians;
using ServiceOps.Domain.WorkOrders;
using Xunit;

namespace ServiceOps.Domain.Tests;

public sealed class WorkOrderWorkflowTests
{
    private readonly Guid actor = Guid.NewGuid();
    private readonly TimeProvider clock = TimeProvider.System;
    private readonly Technician technician = new(Guid.NewGuid(), "Alex Reed", "alex@example.test");
    private WorkOrder Create()
    {
        var customer = new Customer(Guid.NewGuid(), "CEDAR", "Cedar Vale");
        return WorkOrder.Create(customer, new Location(Guid.NewGuid(), customer.Id, "HQ", "Headquarters", "100 Main St"),
            ServiceType.HVAC, Priority.High, "Cooling failure", "East wing is warm", actor, clock);
    }

    [Fact]
    public void Lifecycle_records_changes_and_preserves_classification_and_original_sla()
    {
        var order = Create();
        var original = (order.LocationId, order.ServiceType, order.Priority, order.CreatedAt, order.SlaDeadlineAt, order.SlaAtRiskAt, order.SlaRevision);
        order.Assign(technician, actor, clock);
        order.StartWork(actor, clock);
        order.UpdateDetails(" Corrected title ", " Corrected description ", actor, clock);
        order.PlaceOnHold(" Awaiting access ", actor, clock);
        Assert.Equal("Awaiting access", order.HoldReason);
        order.Resume(actor, clock);
        Assert.Null(order.HoldReason);
        order.Complete(" Replaced failed valve ", actor, clock);
        Assert.Equal(WorkOrderStatus.Completed, order.Status);
        Assert.Equal("Replaced failed valve", order.ResolutionSummary);
        Assert.Equal(order.UpdatedAt, order.CompletedAt);
        Assert.Null(order.GetSlaState(order.SlaDeadlineAt.AddDays(1)));
        Assert.Equal(7, order.Revision);
        Assert.Equal(7, order.Activities.Count);
        Assert.Equal(original, (order.LocationId, order.ServiceType, order.Priority, order.CreatedAt, order.SlaDeadlineAt, order.SlaAtRiskAt, order.SlaRevision));
        Assert.All(order.Activities, x => Assert.Equal(actor, x.ActorUserId));
    }

    [Fact]
    public void Assignment_unassignment_and_reassignment_obey_workflow_and_noops_do_not_audit()
    {
        var order = Create();
        order.Unassign(actor, clock);
        order.Assign(technician, actor, clock);
        order.Assign(technician, actor, clock);
        order.UpdateDetails(order.Title, order.Description, actor, clock);
        Assert.Equal(2, order.Revision);
        order.Unassign(actor, clock);
        Assert.Null(order.TechnicianId);
        Assert.Equal(WorkOrderStatus.New, order.Status);
        order.Assign(technician, actor, clock);
        order.StartWork(actor, clock);
        Assert.Throws<InvalidOperationException>(() => order.Unassign(actor, clock));
        var replacement = new Technician(Guid.NewGuid(), "Morgan Lee", "morgan@example.test");
        order.Assign(replacement, actor, clock);
        Assert.Equal(WorkOrderStatus.InProgress, order.Status);
        Assert.Equal(replacement.Id, order.TechnicianId);
        order.PlaceOnHold("Waiting for part", actor, clock);
        Assert.Throws<InvalidOperationException>(() => order.Unassign(actor, clock));
        order.Assign(technician, actor, clock);
        Assert.Equal(WorkOrderStatus.OnHold, order.Status);
        order.Complete("Part fitted", actor, clock);
        Assert.Equal(WorkOrderStatus.Completed, order.Status);
    }

    [Theory]
    [InlineData(WorkOrderStatus.New)]
    [InlineData(WorkOrderStatus.Assigned)]
    [InlineData(WorkOrderStatus.InProgress)]
    [InlineData(WorkOrderStatus.OnHold)]
    public void Every_open_status_can_cancel_and_terminal_order_rejects_all_mutations(WorkOrderStatus status)
    {
        var order = InStatus(status);
        order.Cancel("Duplicate request", actor, clock);
        Assert.Equal(order.UpdatedAt, order.CancelledAt);
        Assert.Null(order.HoldReason);
        AssertTerminal(order);
    }

    [Fact]
    public void Completion_is_terminal()
    {
        var order = InStatus(WorkOrderStatus.InProgress);
        order.Complete("Repair tested", actor, clock);
        AssertTerminal(order);
    }

    [Fact]
    public void Invalid_paths_and_required_text_are_rejected_without_mutation()
    {
        var order = Create();
        Assert.Throws<InvalidOperationException>(() => order.StartWork(actor, clock));
        Assert.Throws<InvalidOperationException>(() => order.PlaceOnHold("Reason", actor, clock));
        Assert.Throws<InvalidOperationException>(() => order.Resume(actor, clock));
        Assert.Throws<InvalidOperationException>(() => order.Complete("Done", actor, clock));
        Assert.Throws<ArgumentException>(() => order.Cancel(" ", actor, clock));
        Assert.Throws<ArgumentException>(() => order.UpdateDetails("", "Description", actor, clock));
        Assert.Equal(1, order.Revision);
        order.Assign(technician, actor, clock);
        Assert.Throws<InvalidOperationException>(() => order.Complete("Done", actor, clock));
        order.StartWork(actor, clock);
        Assert.Throws<InvalidOperationException>(() => order.StartWork(actor, clock));
        Assert.Throws<ArgumentException>(() => order.PlaceOnHold(" ", actor, clock));
        Assert.Throws<ArgumentException>(() => order.Complete(" ", actor, clock));
        Assert.Equal(3, order.Revision);
    }

    private WorkOrder InStatus(WorkOrderStatus status)
    {
        var order = Create();
        if (status != WorkOrderStatus.New) order.Assign(technician, actor, clock);
        if (status is WorkOrderStatus.InProgress or WorkOrderStatus.OnHold) order.StartWork(actor, clock);
        if (status == WorkOrderStatus.OnHold) order.PlaceOnHold("Awaiting access", actor, clock);
        return order;
    }

    private void AssertTerminal(WorkOrder order)
    {
        var revision = order.Revision;
        Assert.Throws<InvalidOperationException>(() => order.Assign(technician, actor, clock));
        Assert.Throws<InvalidOperationException>(() => order.Unassign(actor, clock));
        Assert.Throws<InvalidOperationException>(() => order.UpdateDetails("Title", "Description", actor, clock));
        Assert.Throws<InvalidOperationException>(() => order.StartWork(actor, clock));
        Assert.Throws<InvalidOperationException>(() => order.PlaceOnHold("Reason", actor, clock));
        Assert.Throws<InvalidOperationException>(() => order.Resume(actor, clock));
        Assert.Throws<InvalidOperationException>(() => order.Complete("Summary", actor, clock));
        Assert.Throws<InvalidOperationException>(() => order.Cancel("Reason", actor, clock));
        Assert.Equal(revision, order.Revision);
    }
}
