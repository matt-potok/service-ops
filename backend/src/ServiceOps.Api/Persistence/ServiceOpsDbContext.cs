using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ServiceOps.Api.Identity;
using ServiceOps.Api.Persistence.Seeding;
using ServiceOps.Domain.Customers;
using ServiceOps.Domain.Technicians;
using ServiceOps.Domain.WorkOrders;

namespace ServiceOps.Api.Persistence;

public sealed class ServiceOpsDbContext(DbContextOptions<ServiceOpsDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<Technician> Technicians => Set<Technician>();
    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();
    public DbSet<WorkOrderActivity> WorkOrderActivities => Set<WorkOrderActivity>();
    public DbSet<DemoSeedState> DemoSeedStates => Set<DemoSeedState>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<ApplicationUser>().Property(user => user.DisplayName).HasMaxLength(120);
        builder.Entity<DemoSeedState>().HasKey(x => x.Version);
        builder.Entity<DemoSeedState>().Property(x => x.Version).HasMaxLength(40);
        builder.ApplyConfigurationsFromAssembly(typeof(ServiceOpsDbContext).Assembly);
        builder.HasSequence<long>("WorkOrderNumbers").StartsAt(10001);
    }
}
