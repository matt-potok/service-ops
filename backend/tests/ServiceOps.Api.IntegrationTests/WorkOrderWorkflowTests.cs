using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using ServiceOps.Api.Features.WorkOrders;
using ServiceOps.Api.Persistence;
using ServiceOps.Domain.WorkOrders;
using Xunit;

namespace ServiceOps.Api.IntegrationTests;

public sealed partial class WorkOrderApiTests
{
    private readonly CompetingWrites competingWrites = new();
    private const string TechnicianOne = "00000003-0000-0000-0000-000000000001";
    private const string TechnicianTwo = "00000003-0000-0000-0000-000000000002";
    private async Task<WorkOrderDetail> CreateOrder() => (await (await client.PostAsJsonAsync("/api/v1/work-orders", ValidInput())).Content.ReadFromJsonAsync<WorkOrderDetail>(json))!;
    private async Task<WorkOrderDetail> Changed(HttpResponseMessage response)
    {
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<WorkOrderDetail>(json))!;
    }
    private Task<HttpResponseMessage> Transition(Guid id, string status, int? revision = null, string? reason = null, string? summary = null) =>
        client.PostAsJsonAsync($"/api/v1/work-orders/{id}/status-transitions", new { targetStatus = status, expectedRevision = revision, reason, summary });

    [Fact]
    public async Task Migration_preserves_existing_New_orders_and_backfills_updated_time()
    {
        var order = await CreateOrder();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceOpsDbContext>();
        // This isolated database contains only Phase 2-compatible New/Created rows.
        await db.GetService<IMigrator>().MigrateAsync("20260929185745_WorkOrderCreation");
        await db.Database.MigrateAsync();
        var stored = (await client.GetFromJsonAsync<WorkOrderDetail>($"/api/v1/work-orders/{order.Id}", json))!;
        Assert.Equal(order, stored);
        Assert.Equal(stored.CreatedAt, stored.UpdatedAt);
        Assert.Equal(1, await db.WorkOrderActivities.CountAsync());
    }

    [Fact]
    public async Task Lifecycle_activity_actor_revision_and_queue_are_consistent()
    {
        var order = await CreateOrder();
        var path = $"/api/v1/work-orders/{order.Id}";
        order = await Changed(await client.PutAsJsonAsync(path + "/assignment", new { technicianId = TechnicianOne, expectedRevision = 1 }));
        Assert.Equal(WorkOrderStatus.Assigned, order.Status);
        Assert.Equal(2, order.Revision);
        order = await Changed(await client.PutAsJsonAsync(path + "/assignment", new { technicianId = TechnicianTwo, expectedRevision = 2 }));
        Assert.Equal(Guid.Parse(TechnicianTwo), order.TechnicianId);
        order = await Changed(await client.PutAsJsonAsync(path + "/assignment", new { technicianId = (string?)null, expectedRevision = 3 }));
        Assert.Equal(WorkOrderStatus.New, order.Status);
        Assert.Null(order.TechnicianId);
        order = await Changed(await client.PutAsJsonAsync(path + "/assignment", new { technicianId = TechnicianOne, expectedRevision = 4 }));
        order = await Changed(await Transition(order.Id, "InProgress", 5));
        order = await Changed(await client.PatchAsJsonAsync(path, new { title = "Corrected cooling issue", description = order.Description, expectedRevision = 6 }));
        order = await Changed(await Transition(order.Id, "OnHold", 7, "Awaiting roof access"));
        Assert.Equal("Awaiting roof access", order.HoldReason);
        var queue = (await client.GetFromJsonAsync<WorkOrderPage>($"/api/v1/work-orders?technicianId={TechnicianOne}&status=OnHold&status=New&openOnly=true", json))!;
        Assert.Equal(order.Id, Assert.Single(queue.Items).Id);
        Assert.NotNull(queue.Items[0].TechnicianName);
        order = await Changed(await Transition(order.Id, "InProgress", 8));
        Assert.Null(order.HoldReason);
        clock.Now = clock.Now.AddHours(1);
        order = await Changed(await Transition(order.Id, "Completed", 9, summary: "Replaced control relay and tested cooling"));
        Assert.Equal(10, order.Revision);
        Assert.Equal(clock.Now, order.CompletedAt);
        Assert.Null(order.SlaState);
        Assert.Empty((await client.GetFromJsonAsync<WorkOrderPage>("/api/v1/work-orders?openOnly=true", json))!.Items);
        Assert.Empty((await client.GetFromJsonAsync<WorkOrderPage>("/api/v1/work-orders?unassigned=true", json))!.Items);
        Assert.Single((await client.GetFromJsonAsync<WorkOrderPage>("/api/v1/work-orders?unassigned=false&status=Completed", json))!.Items);
        var items = new List<ActivityItem>();
        string? cursor = null;
        do
        {
            var page = (await client.GetFromJsonAsync<ActivityPage>(path + "/activity?pageSize=3" + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor)), json))!;
            items.AddRange(page.Items); cursor = page.NextCursor;
        } while (cursor is not null);
        Assert.Equal(10, items.Count);
        Assert.Equal(10, items.Select(x => x.Id).Distinct().Count());
        Assert.All(items, x => { Assert.Equal(actor, x.ActorUserId); Assert.Equal("Elena Brooks", x.ActorName); });
        var correction = Assert.Single(items, x => x.EventType == WorkOrderEventType.DetailsCorrected);
        Assert.Equal("Corrected cooling issue", correction.Changes!.Value.GetProperty("title").GetProperty("after").GetString());
        Assert.False(correction.Changes.Value.TryGetProperty("description", out _));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(path + "/activity?cursor=bad")).StatusCode);
    }

    [Fact]
    public async Task Stale_client_gets_409_but_omitted_revision_uses_current_state_and_noop_does_not_audit()
    {
        var order = await CreateOrder();
        var path = $"/api/v1/work-orders/{order.Id}";
        await Changed(await client.PatchAsJsonAsync(path, new { title = "Latest title", description = order.Description, expectedRevision = 1 }));
        var stale = await client.PatchAsJsonAsync(path, new { title = "Stale title", description = order.Description, expectedRevision = 1 });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Contains("stale_revision", await stale.Content.ReadAsStringAsync());
        var current = await Changed(await client.PatchAsJsonAsync(path, new { title = "Current-state edit", description = order.Description }));
        Assert.Equal(3, current.Revision);
        var noop = await Changed(await client.PatchAsJsonAsync(path, new { title = current.Title, description = current.Description, expectedRevision = 3 }));
        Assert.Equal(3, noop.Revision);
        Assert.Equal(3, (await client.GetFromJsonAsync<ActivityPage>(path + "/activity", json))!.Items.Length);
    }

    [Fact]
    public async Task Competing_HTTP_writes_loaded_at_the_same_revision_have_exactly_one_winner()
    {
        var order = await CreateOrder();
        competingWrites.Enabled = true;
        var path = $"/api/v1/work-orders/{order.Id}";
        var responses = await Task.WhenAll(
            client.PatchAsJsonAsync(path, new { title = "Writer one", description = order.Description, expectedRevision = 1 }),
            client.PatchAsJsonAsync(path, new { title = "Writer two", description = order.Description, expectedRevision = 1 }));
        competingWrites.Enabled = false;
        var winner = await Changed(Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK));
        var loser = Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        Assert.Contains("stale_revision", await loser.Content.ReadAsStringAsync());
        var stored = (await client.GetFromJsonAsync<WorkOrderDetail>(path, json))!;
        Assert.Equal(winner.Title, stored.Title);
        Assert.Equal(2, stored.Revision);
        var activity = (await client.GetFromJsonAsync<ActivityPage>(path + "/activity", json))!;
        Assert.Equal(2, activity.Items.Length);
        Assert.Equal(winner.Title, Assert.Single(activity.Items, x => x.EventType == WorkOrderEventType.DetailsCorrected).Changes!.Value.GetProperty("title").GetProperty("after").GetString());
    }

    [Fact]
    public async Task Activity_failure_rolls_back_business_update_and_revision()
    {
        var order = await CreateOrder();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceOpsDbContext>();
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"WorkOrderActivities\" ADD CONSTRAINT \"RejectActivityForTest\" CHECK (false) NOT VALID");
        var path = $"/api/v1/work-orders/{order.Id}";
        Assert.Equal(HttpStatusCode.InternalServerError, (await client.PatchAsJsonAsync(path, new { title = "Should rollback", description = order.Description, expectedRevision = 1 })).StatusCode);
        Assert.Equal(order, await client.GetFromJsonAsync<WorkOrderDetail>(path, json));
        Assert.Equal(1, await db.WorkOrderActivities.CountAsync());
    }

    [Theory]
    [InlineData("Cancelled")]
    [InlineData("Completed")]
    public async Task Terminal_orders_reject_details_assignment_and_transitions(string terminal)
    {
        var order = await CreateOrder();
        var path = $"/api/v1/work-orders/{order.Id}";
        if (terminal == "Completed")
        {
            await Changed(await client.PutAsJsonAsync(path + "/assignment", new { technicianId = TechnicianOne }));
            await Changed(await Transition(order.Id, "InProgress"));
        }
        await Changed(await Transition(order.Id, terminal, reason: "Duplicate", summary: "Cooling restored"));
        var responses = new[] {
            await client.PatchAsJsonAsync(path, new { title = "Not allowed", description = order.Description }),
            await client.PutAsJsonAsync(path + "/assignment", new { technicianId = TechnicianTwo }),
            await Transition(order.Id, "InProgress"), await Transition(order.Id, "Cancelled", reason: "Again") };
        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Contains("invalid_transition", await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task Invalid_workflow_unknown_inactive_technicians_and_authoritative_fields_are_rejected()
    {
        var order = await CreateOrder();
        var path = $"/api/v1/work-orders/{order.Id}";
        Assert.Equal(HttpStatusCode.Conflict, (await Transition(order.Id, "Completed", summary: "Too soon")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Transition(order.Id, "Cancelled", reason: " ")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(path + "/assignment", new { technicianId = Guid.NewGuid() })).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceOpsDbContext>();
        await db.Technicians.Where(x => x.Id == Guid.Parse(TechnicianTwo)).ExecuteUpdateAsync(x => x.SetProperty(t => t.IsActive, false));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(path + "/assignment", new { technicianId = TechnicianTwo })).StatusCode);
        foreach (var field in new[] { "priority", "status", "locationId", "serviceType", "customerId", "actorUserId", "createdAt", "revision", "slaDeadlineAt" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync(path, new Dictionary<string, object> { ["title"] = "Correction", ["description"] = order.Description, [field] = "injected" })).StatusCode);
        await Changed(await client.PutAsJsonAsync(path + "/assignment", new { technicianId = TechnicianOne }));
        await Changed(await Transition(order.Id, "InProgress"));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(path + "/assignment", new { technicianId = (string?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Transition(order.Id, "OnHold", reason: " ")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Transition(order.Id, "Completed", summary: " ")).StatusCode);
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PatchAsJsonAsync(path, new { title = "Correction", description = order.Description })).StatusCode);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await Transition(order.Id, "Cancelled", reason: "Duplicate")).StatusCode);
    }

    // Test-only barrier makes both real HTTP requests reach SaveChanges with revision 1 loaded.
    private sealed class CompetingWrites : SaveChangesInterceptor
    {
        public bool Enabled { get; set; }
        private int arrived;
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Enabled)
            {
                if (Interlocked.Increment(ref arrived) == 2) ready.TrySetResult();
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }
            return result;
        }
    }
}
