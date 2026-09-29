using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServiceOps.Domain.Customers;
using ServiceOps.Domain.Technicians;

namespace ServiceOps.Api.Persistence.Configurations;

public sealed class ReferenceDataConfiguration : IEntityTypeConfiguration<Customer>, IEntityTypeConfiguration<Location>, IEntityTypeConfiguration<Technician>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.Property(x => x.Code).HasMaxLength(30);
        builder.Property(x => x.Name).HasMaxLength(200);
        builder.HasIndex(x => x.Code).IsUnique();
    }

    public void Configure(EntityTypeBuilder<Location> builder)
    {
        builder.Property(x => x.Code).HasMaxLength(30);
        builder.Property(x => x.Name).HasMaxLength(200);
        builder.Property(x => x.Address).HasMaxLength(300);
        builder.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.CustomerId, x.Code }).IsUnique();
    }

    public void Configure(EntityTypeBuilder<Technician> builder)
    {
        builder.Property(x => x.DisplayName).HasMaxLength(120);
        builder.Property(x => x.Email).HasMaxLength(254);
    }
}
