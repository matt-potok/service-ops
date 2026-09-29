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
            table.HasCheckConstraint("CK_WorkOrder_Status", "\"Status\" = 'New'");
            table.HasCheckConstraint("CK_WorkOrder_Revisions", "\"Revision\" > 0 AND \"SlaRevision\" > 0 AND \"SlaPolicyVersion\" > 0");
        });
    }

    public void Configure(EntityTypeBuilder<WorkOrderActivity> builder)
    {
        builder.Property(x => x.EventType).HasConversion<string>().HasMaxLength(30);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(table => table.HasCheckConstraint("CK_Activity_EventType", "\"EventType\" = 'Created'"));
    }
}
