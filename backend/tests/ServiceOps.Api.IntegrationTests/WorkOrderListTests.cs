using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceOps.Api.Features.WorkOrders;
using ServiceOps.Api.Persistence;
using ServiceOps.Domain.WorkOrders;
using Xunit;

namespace ServiceOps.Api.IntegrationTests;

public sealed partial class WorkOrderApiTests
{
    [Fact]
    public async Task List_paginates_with_stable_ties_and_business_priority_sort()
    {
        await AddQueueOrders(61);
        var first = await List();
        var second = await List("page=2&pageSize=25");
        Assert.Equal(61, first.TotalCount);
        Assert.Equal(25, first.Items.Length);
        Assert.Equal(1, first.Page);
        Assert.Equal(25, first.PageSize);
        Assert.Equal(clock.Now, first.EvaluatedAt);
        Assert.Empty(first.Items.Select(x => x.Id).Intersect(second.Items.Select(x => x.Id)));
        Assert.Equal(first.Items.Select(x => x.Id), (await List()).Items.Select(x => x.Id));
        var all = await List("pageSize=100");
        Assert.Equal(all.Items.Select(x => x.CreatedAt).OrderDescending(), all.Items.Select(x => x.CreatedAt));
        Assert.Equal(all.Items.Take(50).Select(x => x.Id), first.Items.Concat(second.Items).Select(x => x.Id));
        Assert.Equal(11, (await List("page=2&pageSize=50")).Items.Length);
        var priorities = (await List("sort=priority&pageSize=100")).Items.Select(x => x.Priority).ToArray();
        Assert.Equal(priorities.OrderBy(x => (int)x), priorities);
        var descending = (await List("sort=-priority&pageSize=100")).Items.Select(x => x.Priority).ToArray();
        Assert.Equal(priorities.Reverse(), descending);
        foreach (var sort in new[] { "number", "createdAt", "deadline", "status", "customerName" })
        {
            Assert.Equal(61, (await List($"sort={sort}&pageSize=100")).Items.Length);
            Assert.Equal(61, (await List($"sort=-{sort}&pageSize=100")).Items.Length);
        }
        Assert.Empty((await List("page=99")).Items);
    }

    [Fact]
    public async Task List_combines_search_reference_service_and_repeated_status_filters()
    {
        var orders = await AddQueueOrders(12);
        var target = orders[0];
        var customerId = target.Location.CustomerId;
        Assert.Equal(6, (await List($"customerId={customerId}")).TotalCount);
        Assert.Equal(6, (await List($"locationId={target.LocationId}")).TotalCount);
        Assert.Equal(6, (await List("serviceType=HVAC")).TotalCount);
        Assert.Equal(12, (await List("status=New&status=New&openOnly=true")).TotalCount);
        Assert.Equal(12, (await List("search=cOoLiNg")).TotalCount);
        var exactNumber = await List($"search={target.Number}");
        Assert.Equal(target.Id, Assert.Single(exactNumber.Items).Id);
        Assert.Equal(6, (await List($"search=cooling&customerId={customerId}&locationId={target.LocationId}&serviceType=HVAC&status=New&status=New")).TotalCount);
        Assert.Empty((await List($"customerId={customerId}&locationId={orders[1].LocationId}")).Items);
        Assert.Empty((await List("search=%25")).Items); // Literal %, not a wildcard.
        Assert.Empty((await List("search=%5F")).Items);
    }

    [Fact]
    public async Task List_sla_filters_agree_with_domain_at_exact_boundaries_without_worker()
    {
        var orders = await AddQueueOrders(4);
        var target = orders[0];
        foreach (var instant in new[] { target.SlaAtRiskAt.AddTicks(-10), target.SlaAtRiskAt, target.SlaDeadlineAt.AddTicks(-10), target.SlaDeadlineAt })
        {
            clock.Now = instant;
            foreach (var state in Enum.GetValues<SlaState>())
            {
                var result = await List($"slaStatus={state}");
                Assert.Equal(instant, result.EvaluatedAt);
                Assert.Equal(orders.Where(x => x.GetSlaState(instant) == state).Select(x => x.Id).Order(), result.Items.Select(x => x.Id).Order());
                Assert.All(result.Items, x => Assert.Equal(state, x.SlaState));
            }
        }
    }

    [Fact]
    public async Task List_created_range_is_inclusive_start_and_exclusive_end()
    {
        var orders = await AddQueueOrders(6);
        var start = orders.Min(x => x.CreatedAt);
        var end = orders.Max(x => x.CreatedAt);
        var result = await List($"createdFrom={Uri.EscapeDataString(start.ToString("O"))}&createdTo={Uri.EscapeDataString(end.ToString("O"))}");
        Assert.Equal(orders.Count(x => x.CreatedAt >= start && x.CreatedAt < end), result.TotalCount);
        Assert.All(result.Items, x => Assert.True(x.CreatedAt >= start && x.CreatedAt < end));
        var offset = start.ToOffset(TimeSpan.FromHours(-4));
        Assert.Equal(6, (await List($"createdFrom={Uri.EscapeDataString(offset.ToString("O"))}")).TotalCount);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("page=2147483647&pageSize=100")]
    [InlineData("pageSize=101")]
    [InlineData("pageSize=26")]
    [InlineData("sort=description")]
    [InlineData("sort=--priority")]
    [InlineData("serviceType=Unknown")]
    [InlineData("status=Completed")]
    [InlineData("status=99")]
    [InlineData("slaStatus=Unknown")]
    [InlineData("customerId=bad")]
    [InlineData("createdFrom=not-a-date")]
    [InlineData("createdFrom=2026-09-29")]
    [InlineData("createdFrom=2026-09-30T00:00:00Z&createdTo=2026-09-29T00:00:00Z")]
    [InlineData("technicianId=00000003-0000-0000-0000-000000000001&unassigned=true")]
    [InlineData("unassigned=false")]
    [InlineData("completedFrom=2026-09-29T00:00:00Z")]
    [InlineData("attentionOnly=true")]
    public async Task List_rejects_invalid_or_not_yet_supported_filters(string query)
    {
        var response = await client.GetAsync($"/api/v1/work-orders?{query}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("errors", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task List_empty_search_bound_and_anonymous_access()
    {
        Assert.Empty((await List()).Items);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/work-orders?search=" + new string('a', 201))).StatusCode);
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/work-orders")).StatusCode);
    }

    private async Task<WorkOrderPage> List(string query = "")
    {
        var response = await client.GetAsync("/api/v1/work-orders?" + query);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<WorkOrderPage>(json))!;
    }

    private async Task<WorkOrder[]> AddQueueOrders(int count)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceOpsDbContext>();
        var locations = await db.Locations.Include(x => x.Customer).OrderBy(x => x.Id).ToArrayAsync();
        var now = clock.Now;
        var orders = Enumerable.Range(0, count).Select(i =>
        {
            var site = locations[i % 2 == 0 ? 0 : 3];
            clock.Now = now.AddMinutes(-(i / 3)); // Deliberate ties to exercise stable pagination.
            return WorkOrder.Create(site.Customer, site, i % 2 == 0 ? ServiceType.HVAC : ServiceType.Electrical,
                (Priority)(i % 4), $"Cooling inspection {i:D2}", "Inspect the service area.", actor, clock);
        }).ToArray();
        clock.Now = now;
        db.WorkOrders.AddRange(orders);
        await db.SaveChangesAsync();
        return orders;
    }
}
