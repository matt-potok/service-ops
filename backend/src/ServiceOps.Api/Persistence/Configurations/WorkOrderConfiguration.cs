using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServiceOps.Api.Identity;
using ServiceOps.Domain.WorkOrders;

namespace ServiceOps.Api.Persistence.Configurations;

public sealed class WorkOrderConfiguration : IEntityTypeConfiguration<WorkOrder>, IEntityTypeConfiguration<WorkOrderActivity>
{
    public void Configure(EntityTypeBuilder<WorkOrder> builder)
    {
        builder.Property(x => x.Number).HasMaxLength(30).HasSentinel("").HasDefaultValueSql("'WO-' || nextval('\"WorkOrderNumbers\"')");
        builder.HasIndex(x => x.Number).IsUnique();
        builder.Property(x => x.Title).HasMaxLength(200);
        builder.Property(x => x.Description).HasMaxLength(10000);
        builder.Property(x => x.Priority).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.ServiceType).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Revision).IsConcurrencyToken();
        builder.Property(x => x.HoldReason).HasMaxLength(5000);
        builder.Property(x => x.ResolutionSummary).HasMaxLength(5000);
        builder.Property(x => x.CancellationReason).HasMaxLength(5000);
        builder.HasOne(x => x.Technician).WithMany().HasForeignKey(x => x.TechnicianId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Activities).WithOne().HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(x => x.Activities).HasField("activities").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_WorkOrder_Text", "length(btrim(\"Title\")) > 0 AND length(btrim(\"Description\")) > 0");
            table.HasCheckConstraint("CK_WorkOrder_Sla", "\"SlaDurationMinutes\" > 0 AND \"CreatedAt\" < \"SlaAtRiskAt\" AND \"SlaAtRiskAt\" < \"SlaDeadlineAt\"");
            table.HasCheckConstraint("CK_WorkOrder_Priority", "\"Priority\" IN ('Critical', 'High', 'Normal', 'Low')");
            table.HasCheckConstraint("CK_WorkOrder_ServiceType", "\"ServiceType\" IN ('HVAC', 'Electrical', 'Plumbing', 'Equipment', 'GeneralMaintenance')");
            table.HasCheckConstraint("CK_WorkOrder_Status", "\"Status\" IN ('New', 'Assigned', 'InProgress', 'OnHold', 'Completed', 'Cancelled')");
            table.HasCheckConstraint("CK_WorkOrder_Assignment", "(\"Status\" <> 'New' OR \"TechnicianId\" IS NULL) AND (\"Status\" NOT IN ('Assigned', 'InProgress', 'OnHold', 'Completed') OR \"TechnicianId\" IS NOT NULL)");
            table.HasCheckConstraint("CK_WorkOrder_Terminal", "((\"Status\" = 'Completed' AND \"CompletedAt\" IS NOT NULL AND \"CompletedAt\" >= \"CreatedAt\" AND length(btrim(coalesce(\"ResolutionSummary\", ''))) > 0) OR (\"Status\" <> 'Completed' AND \"CompletedAt\" IS NULL AND \"ResolutionSummary\" IS NULL)) AND ((\"Status\" = 'Cancelled' AND \"CancelledAt\" IS NOT NULL AND \"CancelledAt\" >= \"CreatedAt\" AND length(btrim(coalesce(\"CancellationReason\", ''))) > 0) OR (\"Status\" <> 'Cancelled' AND \"CancelledAt\" IS NULL AND \"CancellationReason\" IS NULL))");
            table.HasCheckConstraint("CK_WorkOrder_Hold", "(\"Status\" = 'OnHold' AND length(btrim(coalesce(\"HoldReason\", ''))) > 0) OR (\"Status\" <> 'OnHold' AND \"HoldReason\" IS NULL)");
            table.HasCheckConstraint("CK_WorkOrder_Revisions", "\"Revision\" > 0 AND \"SlaRevision\" > 0 AND \"SlaPolicyVersion\" > 0");
        });
    }

    public void Configure(EntityTypeBuilder<WorkOrderActivity> builder)
    {
        builder.Property(x => x.EventType).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.Changes).HasColumnType("jsonb");
        builder.HasIndex(x => new { x.WorkOrderId, x.EffectiveAt, x.Id });
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(table => table.HasCheckConstraint("CK_Activity_EventType", "\"EventType\" IN ('Created', 'Assigned', 'Reassigned', 'Unassigned', 'DetailsCorrected', 'Started', 'PlacedOnHold', 'Resumed', 'Completed', 'Cancelled')"));
    }
}
