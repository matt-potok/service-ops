using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ServiceOps.Api.Persistence;
using ServiceOps.Api.Persistence.Seeding;
using ServiceOps.Domain.WorkOrders;
using Xunit;

namespace ServiceOps.Api.IntegrationTests;

public sealed partial class WorkOrderApiTests
{
    [Fact]
    public async Task Demo_seed_has_useful_distributions_and_valid_domain_histories()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceOpsDbContext>();
        await PortfolioDemoSeed.SeedAsync(db, new ConfigurationBuilder().Build());
        db.ChangeTracker.Clear();
        var orders = await db.WorkOrders.Include(x => x.Activities).ToArrayAsync();
        var anchor = PortfolioDemoSeed.DefaultAnchor;
        Assert.Equal(20, await db.Customers.CountAsync());
        Assert.Equal(50, await db.Locations.CountAsync());
        Assert.Equal(15, await db.Technicians.CountAsync());
        Assert.InRange(orders.Length, 400, 500);
        Assert.Equal(6, orders.Select(x => x.Status).Distinct().Count());
        Assert.Equal(5, orders.Select(x => x.ServiceType).Distinct().Count());
        Assert.Equal(4, orders.Select(x => x.Priority).Distinct().Count());
        Assert.True(orders.Count(x => x.Priority == Priority.Normal) > orders.Length / 2);
        Assert.InRange(orders.Count(x => x.Priority == Priority.Critical), 1, orders.Length / 10);
        Assert.True(orders.Select(x => x.Title).Distinct().Count() >= 60);
        Assert.Equal(50, orders.Select(x => x.LocationId).Distinct().Count());
        Assert.Equal(15, orders.Where(x => x.TechnicianId != null).Select(x => x.TechnicianId).Distinct().Count());
        Assert.True(orders.Min(x => x.CreatedAt) < anchor.AddMonths(-5));
        Assert.True(orders.Max(x => x.CreatedAt) <= anchor);
        var completed = orders.Where(x => x.Status == WorkOrderStatus.Completed).ToArray();
        Assert.True(completed.Length > orders.Length / 2);
        Assert.Contains(completed, x => x.CompletedAt <= x.SlaDeadlineAt);
        Assert.Contains(completed, x => x.CompletedAt > x.SlaDeadlineAt);
        Assert.InRange(completed.Select(x => new { x.CompletedAt!.Value.Year, x.CompletedAt.Value.Month }).Distinct().Count(), 6, 7);
        var open = orders.Where(x => x.Status is not (WorkOrderStatus.Completed or WorkOrderStatus.Cancelled)).ToArray();
        Assert.Equal(3, open.Select(x => x.GetSlaState(anchor)).Distinct().Count());
        Assert.Contains(open, x => x.CreatedAt < anchor.AddDays(-7));
        Assert.True(orders.All(x => x.Status != WorkOrderStatus.New || x.TechnicianId is null));
        Assert.True(orders.All(x => x.Status is WorkOrderStatus.New or WorkOrderStatus.Cancelled || x.TechnicianId is not null));
        Assert.True(orders.All(x => x.Status != WorkOrderStatus.OnHold || !string.IsNullOrWhiteSpace(x.HoldReason)));
        Assert.True(completed.All(x => x.CompletedAt >= x.CreatedAt && x.CompletedAt <= anchor && !string.IsNullOrWhiteSpace(x.ResolutionSummary)));
        Assert.True(orders.Where(x => x.Status == WorkOrderStatus.Cancelled).All(x => x.CancelledAt >= x.CreatedAt && x.CancelledAt <= anchor && !string.IsNullOrWhiteSpace(x.CancellationReason)));
        Assert.True(orders.All(x => x.Activities.Count == x.Revision && x.SlaRevision == 1));
        Assert.True(orders.All(x => x.Activities.Count(a => a.EventType == WorkOrderEventType.Created) == 1));
        Assert.True(orders.All(x => x.Activities.Min(a => a.EffectiveAt) == x.CreatedAt && x.Activities.Max(a => a.EffectiveAt) == x.UpdatedAt));
        Assert.True(orders.All(x => x.Activities.All(a => a.EffectiveAt == a.RecordedAt && a.EffectiveAt >= x.CreatedAt && a.EffectiveAt <= anchor)));
        Assert.True(completed.All(x => x.Activities.OrderBy(a => a.EffectiveAt).Last().EventType == WorkOrderEventType.Completed));
        Assert.True(orders.All(x => x.SlaDeadlineAt == x.CreatedAt.AddMinutes(WorkOrder.SlaDurationFor(x.Priority))));
        Assert.Single(await db.DemoSeedStates.ToArrayAsync());
    }

    [Fact]
    public async Task Demo_seed_is_repeatable_on_fresh_databases_and_reruns_preserve_edits()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["DemoSeed:AnchorUtc"] = "2026-10-02T12:30:00Z" }).Build();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceOpsDbContext>();
        await PortfolioDemoSeed.SeedAsync(db, configuration);
        var first = await DemoBusinessSnapshot(db);
        var other = new WorkOrderApiTests();
        try
        {
            await other.InitializeAsync();
            await using var secondScope = other.factory.Services.CreateAsyncScope();
            var second = secondScope.ServiceProvider.GetRequiredService<ServiceOpsDbContext>();
            await PortfolioDemoSeed.SeedAsync(second, configuration);
            Assert.Equal(first, await DemoBusinessSnapshot(second));
        }
        finally { await other.DisposeAsync(); }

        var open = await db.WorkOrders.FirstAsync(x => x.Status == WorkOrderStatus.New);
        clock.Now = new DateTimeOffset(2026, 10, 2, 13, 0, 0, TimeSpan.Zero);
        open.UpdateDetails("A reviewer's own correction", open.Description, actor, clock);
        // This context contains the original activities; add only the newly created entry.
        db.WorkOrderActivities.Add(open.Activities.Last());
        await db.SaveChangesAsync();
        var edited = await DemoBusinessSnapshot(db);
        await PortfolioDemoSeed.SeedAsync(db, configuration);
        Assert.Equal(edited, await DemoBusinessSnapshot(db));
        await PortfolioDemoSeed.SeedAsync(db, new ConfigurationBuilder().Build());
        Assert.Equal(edited, await DemoBusinessSnapshot(db));
        await Assert.ThrowsAsync<InvalidOperationException>(() => PortfolioDemoSeed.SeedAsync(db,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DemoSeed:AnchorUtc"] = "2026-10-03T12:30:00Z" }).Build()));
        Assert.Equal(edited, await DemoBusinessSnapshot(db));
    }

    [Fact]
    public async Task Demo_seed_refuses_unmarked_existing_work_and_invalid_anchor()
    {
        await CreateOrder();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceOpsDbContext>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => PortfolioDemoSeed.SeedAsync(db, new ConfigurationBuilder().Build()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => PortfolioDemoSeed.SeedAsync(db,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DemoSeed:AnchorUtc"] = "not-a-date" }).Build()));
        Assert.Equal(1, await db.WorkOrders.CountAsync());
        Assert.Empty(await db.DemoSeedStates.ToArrayAsync());
    }

    [Fact]
    public async Task Demo_seed_failure_rolls_back_previously_saved_orders_and_marker()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceOpsDbContext>();
        // The first ten sequential order saves succeed; a later insert fails inside the outer transaction.
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"WorkOrders\" ADD CONSTRAINT \"RejectLaterDemoForTest\" CHECK (\"Number\" <> 'WO-10011')");
        await Assert.ThrowsAsync<DbUpdateException>(() => PortfolioDemoSeed.SeedAsync(db, new ConfigurationBuilder().Build()));
        Assert.Equal(0, await db.WorkOrders.CountAsync());
        Assert.Equal(0, await db.WorkOrderActivities.CountAsync());
        Assert.Empty(await db.DemoSeedStates.ToArrayAsync());
    }

    private static async Task<string[]> DemoBusinessSnapshot(ServiceOpsDbContext db)
    {
        var actors = await db.Users.ToDictionaryAsync(x => x.Id, x => x.DisplayName);
        var orders = await db.WorkOrders.AsNoTracking().Include(x => x.Activities).OrderBy(x => x.Number).ToArrayAsync();
        // Exclude only generated aggregate/activity/user UUIDs. Include numbers, content, references,
        // timestamps and known activity payloads so accidental random iteration/order changes are caught.
        return orders.Select(x => System.Text.Json.JsonSerializer.Serialize(new {
            x.Number, x.LocationId, x.TechnicianId, x.Title, x.Description, x.Priority, x.ServiceType, x.Status,
            x.CreatedAt, x.UpdatedAt, x.CompletedAt, x.CancelledAt, x.HoldReason, x.ResolutionSummary, x.CancellationReason,
            x.SlaAtRiskAt, x.SlaDeadlineAt, x.Revision, Creator = actors[x.CreatedByUserId],
            Activity = x.Activities.OrderBy(a => a.EffectiveAt).Select(a => new { a.EventType, a.EffectiveAt, a.RecordedAt, a.Changes, Actor = actors[a.ActorUserId] })
        })).ToArray();
    }
}
