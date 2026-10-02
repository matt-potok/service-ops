using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceOps.Api.Features.Auth;
using ServiceOps.Api.Features.Dashboard;
using ServiceOps.Api.Persistence;
using ServiceOps.Domain.WorkOrders;
using Xunit;

namespace ServiceOps.Api.IntegrationTests;

public sealed partial class WorkOrderApiTests
{
    [Fact]
    public async Task Dashboard_requires_manager_role_and_returns_empty_metrics_safely()
    {
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/dashboard")).StatusCode);
        await DashboardManagerSignIn();
        var result = await ReadDashboard();
        Assert.Equal(0, result.Current.Open);
        Assert.Equal(0, result.Period.Completed);
        Assert.Equal(0, result.Period.Met);
        Assert.Equal(0, result.Period.Missed);
        Assert.Null(result.Period.CompliancePercent);
        Assert.Equal(new DateOnly(2026, 8, 31), result.Period.StartDate);
        Assert.Equal(new DateOnly(2026, 9, 30), result.Period.EndDateExclusive);
        Assert.Equal("America/New_York", result.TimeZone);
        var unassigned = Assert.Single(result.Workload);
        Assert.Null(unassigned.TechnicianId);
        Assert.Equal(0, unassigned.OpenCount);
    }

    [Fact]
    public async Task Dashboard_reconciles_open_status_sla_workload_and_completion_cohorts()
    {
        var now = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ServiceOpsDbContext>();
            var site = await db.Locations.Include(x => x.Customer).FirstAsync();
            var tech = await db.Technicians.OrderBy(x => x.Id).FirstAsync();
            WorkOrder Open(DateTimeOffset created, WorkOrderStatus status)
            {
                clock.Now = created;
                var order = WorkOrder.Create(site.Customer, site, ServiceType.HVAC, Priority.Normal, "Inspect cooling unit", "Inspect the reported fault.", actor, clock);
                if (status != WorkOrderStatus.New) { clock.Now = created.AddMinutes(1); order.Assign(tech, actor, clock); }
                if (status is WorkOrderStatus.InProgress or WorkOrderStatus.OnHold) { clock.Now = created.AddMinutes(2); order.StartWork(actor, clock); }
                if (status == WorkOrderStatus.OnHold) { clock.Now = created.AddMinutes(3); order.PlaceOnHold("Waiting for access.", actor, clock); }
                db.WorkOrders.Add(order);
                return order;
            }
            Open(now.AddHours(-6).AddSeconds(1), WorkOrderStatus.New);
            Open(now.AddHours(-6), WorkOrderStatus.Assigned); // Exact risk boundary.
            Open(now.AddHours(-8), WorkOrderStatus.InProgress); // Exact breach boundary.
            Open(now.AddMonths(-2), WorkOrderStatus.OnHold); // Old backlog outside the period.
            void Complete(DateTimeOffset created, DateTimeOffset finished)
            {
                var order = Open(created, WorkOrderStatus.InProgress);
                clock.Now = finished;
                order.Complete("Replaced the failed component and verified operation.", actor, clock);
            }
            var start = new DateTimeOffset(2026, 10, 1, 4, 0, 0, TimeSpan.Zero); // New York midnight.
            Complete(start.AddHours(-8), start); // Met exactly at deadline and inclusive start; created before period.
            Complete(start.AddMonths(-1), start.AddHours(12)); // Missed, created well before period.
            Complete(start.AddHours(-9), start.AddSeconds(-1)); // Excluded just before start.
            Complete(start.AddHours(20), start.AddDays(1)); // Excluded exactly at end.
            var cancelled = Open(start, WorkOrderStatus.New);
            clock.Now = start.AddHours(1);
            cancelled.Cancel("Duplicate request.", actor, clock);
            await db.SaveChangesAsync();
        }
        clock.Now = now;
        await DashboardManagerSignIn();
        var result = await ReadDashboard("startDate=2026-10-01&endDateExclusive=2026-10-02");
        Assert.Equal(now, result.EvaluatedAt);
        Assert.Equal(new CurrentOperations(4, 1, 1, 1, 1, 1, 1, 2), result.Current);
        Assert.Equal(4, result.Workload.Sum(x => x.OpenCount));
        Assert.Equal(1, result.Workload.Single(x => x.TechnicianId == null).OpenCount);
        Assert.Equal(3, result.Workload.Single(x => x.TechnicianId != null).OpenCount);
        Assert.Equal(2, result.Period.Completed);
        Assert.Equal(1, result.Period.Met);
        Assert.Equal(1, result.Period.Missed);
        Assert.Equal(50m, result.Period.CompliancePercent);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 4, 0, 0, TimeSpan.Zero), result.Period.FromUtc);
        Assert.Equal(new DateTimeOffset(2026, 10, 2, 4, 0, 0, TimeSpan.Zero), result.Period.ToUtc);
        var empty = await ReadDashboard("startDate=2025-01-01&endDateExclusive=2025-02-01");
        Assert.Equal(result.Current, empty.Current);
        Assert.Equal(0, empty.Period.Completed);
        Assert.Null(empty.Period.CompliancePercent);
        Assert.Equal(result.Current.Open, (await List("openOnly=true")).TotalCount);
        foreach (var (state, count) in new[] { ("Good", 1), ("AtRisk", 1), ("Breached", 2) })
            Assert.Equal(count, (await List($"openOnly=true&slaStatus={state}")).TotalCount);
        foreach (var status in new[] { "New", "Assigned", "InProgress", "OnHold" })
            Assert.Equal(1, (await List($"openOnly=true&status={status}")).TotalCount);
        foreach (var row in result.Workload)
            Assert.Equal(row.OpenCount, (await List("openOnly=true&" + (row.TechnicianId is null ? "unassigned=true" : $"technicianId={row.TechnicianId}"))).TotalCount);
    }

    [Fact]
    public async Task Dashboard_uses_reporting_calendar_across_daylight_saving_boundaries()
    {
        await DashboardManagerSignIn();
        var spring = await ReadDashboard("startDate=2026-03-08&endDateExclusive=2026-03-09");
        Assert.Equal(23, (spring.Period.ToUtc - spring.Period.FromUtc).TotalHours);
        Assert.Equal(5, spring.Period.FromUtc.Hour);
        var fall = await ReadDashboard("startDate=2026-11-01&endDateExclusive=2026-11-02");
        Assert.Equal(25, (fall.Period.ToUtc - fall.Period.FromUtc).TotalHours);
        clock.Now = new DateTimeOffset(2026, 10, 2, 2, 0, 0, TimeSpan.Zero); // Still October 1 in New York.
        await DashboardManagerSignIn(); // Changing the shared clock expires the earlier authentication cookie.
        var defaults = await ReadDashboard();
        Assert.Equal(new DateOnly(2026, 9, 2), defaults.Period.StartDate);
        Assert.Equal(new DateOnly(2026, 10, 2), defaults.Period.EndDateExclusive);
    }

    [Fact]
    public async Task Dashboard_rejects_invalid_incomplete_or_unsupported_date_filters()
    {
        await DashboardManagerSignIn();
        string[] invalid = ["startDate=2026-10-01", "endDateExclusive=2026-10-02", "startDate=&endDateExclusive=",
            "startDate=2026-02-30&endDateExclusive=2026-03-02", "startDate=2026-10-02&endDateExclusive=2026-10-02",
            "startDate=2026-10-03&endDateExclusive=2026-10-02", "startDate=2025-01-01&endDateExclusive=2026-10-02",
            "startDate=2026-10-01T00:00:00Z&endDateExclusive=2026-10-02", "customerId=unused",
            "startDate=2026-10-01&startDate=2026-10-02&endDateExclusive=2026-10-03"];
        foreach (var query in invalid)
        {
            var response = await client.GetAsync("/api/v1/dashboard?" + query);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains("errors", await response.Content.ReadAsStringAsync());
        }
    }

    private async Task DashboardManagerSignIn()
    {
        await SetCsrf();
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("marcus.chen@atlas.example", password))).StatusCode);
    }

    private async Task<DashboardResponse> ReadDashboard(string query = "")
    {
        var response = await client.GetAsync("/api/v1/dashboard?" + query);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<DashboardResponse>(json))!;
    }
}
