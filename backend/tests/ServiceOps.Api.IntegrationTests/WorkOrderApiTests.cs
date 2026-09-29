using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ServiceOps.Api.Features.Auth;
using ServiceOps.Api.Features.WorkOrders;
using ServiceOps.Api.Persistence;
using ServiceOps.Api.Persistence.Seeding;
using Xunit;

namespace ServiceOps.Api.IntegrationTests;

public sealed class WorkOrderApiTests : IAsyncLifetime
{
    private readonly string databaseName = "serviceops_test_" + Guid.NewGuid().ToString("N");
    private readonly string adminConnection = Environment.GetEnvironmentVariable("TEST_DATABASE_CONNECTION")
        ?? throw new InvalidOperationException("Set TEST_DATABASE_CONNECTION to a PostgreSQL account with CREATEDB permission.");
    private readonly string password = "Test-only-" + Guid.NewGuid().ToString("N") + "!9";
    private readonly TestClock clock = new();
    private readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private WebApplicationFactory<Program> factory = null!;
    private HttpClient client = null!;
    private Guid actor;

    public async Task InitializeAsync()
    {
        await using var admin = new NpgsqlConnection(adminConnection);
        await admin.OpenAsync();
        await using var create = new NpgsqlCommand($"CREATE DATABASE {databaseName}", admin);
        await create.ExecuteNonQueryAsync();
        var connection = new NpgsqlConnectionStringBuilder(adminConnection) { Database = databaseName };
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:ServiceOps", connection.ConnectionString);
            builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(clock));
        });
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceOpsDbContext>();
        await db.Database.MigrateAsync();
        await DemoUsers.SeedAsync(scope.ServiceProvider, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Seed:OperationsPassword"] = password, ["Seed:ManagerPassword"] = password
        }).Build());
        await ReferenceDataSeed.SeedAsync(db);
        client = factory.CreateClient();
        await SetCsrf();
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest("elena.brooks@atlas.example", password))).StatusCode);
        actor = (await client.GetFromJsonAsync<SessionResponse>("/api/v1/auth/me"))!.Id;
        await SetCsrf();
    }

    private static Dictionary<string, object?> ValidInput() => new()
    {
        ["customerId"] = "00000001-0000-0000-0000-000000000001",
        ["locationId"] = "00000002-0000-0000-0000-000000000001",
        ["serviceType"] = "HVAC", ["priority"] = "High",
        ["title"] = "Rooftop unit not cooling", ["description"] = "East wing temperature is above the set point."
    };

    [Fact]
    public async Task Create_persists_detail_and_activity_and_uses_current_time_on_read()
    {
        var response = await client.PostAsJsonAsync("/api/v1/work-orders", ValidInput());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var detail = (await response.Content.ReadFromJsonAsync<WorkOrderDetail>(json))!;
        Assert.Equal($"/api/v1/work-orders/{detail.Id}", response.Headers.Location!.AbsolutePath);
        Assert.StartsWith("WO-", detail.Number);
        Assert.Equal(clock.Now, detail.CreatedAt);
        Assert.Equal(actor, detail.CreatedByUserId);
        Assert.Equal(clock.Now.AddHours(4), detail.SlaDeadlineAt);
        Assert.Equal(clock.Now.AddHours(3), detail.SlaAtRiskAt);
        Assert.Equal("Harborstone Logistics", detail.CustomerName);
        Assert.Equal("North Distribution Center", detail.LocationName);
        Assert.Equal("Good", detail.SlaState.ToString());
        Assert.Equal(detail, await client.GetFromJsonAsync<WorkOrderDetail>(response.Headers.Location, json));
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceOpsDbContext>();
        var activity = await db.WorkOrderActivities.SingleAsync(x => x.WorkOrderId == detail.Id);
        Assert.Equal(actor, activity.ActorUserId);
        Assert.Equal(detail.CreatedAt, activity.EffectiveAt);
        Assert.Equal(detail.CreatedAt, activity.RecordedAt);
        Assert.Equal("Created", activity.EventType.ToString());
        clock.Now = detail.SlaDeadlineAt;
        var later = (await client.GetFromJsonAsync<WorkOrderDetail>(response.Headers.Location, json))!;
        Assert.Equal("Breached", later.SlaState.ToString());
        Assert.Equal(detail.CreatedAt, later.CreatedAt);
    }

    [Fact]
    public async Task Customer_location_mismatch_is_rejected_without_writes()
    {
        var request = ValidInput();
        request["customerId"] = "00000001-0000-0000-0000-000000000002";
        var response = await client.PostAsJsonAsync("/api/v1/work-orders", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("locationId", await response.Content.ReadAsStringAsync());
        await AssertNoOrders();
    }

    [Theory]
    [InlineData("title", "")]
    [InlineData("description", "   ")]
    [InlineData("priority", null)]
    [InlineData("serviceType", "Unsupported")]
    [InlineData("locationId", "00000000-0000-0000-0000-000000000000")]
    public async Task Invalid_required_input_is_rejected(string field, string? value)
    {
        var request = ValidInput(); request[field] = value;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/work-orders", request)).StatusCode);
        await AssertNoOrders();
    }

    [Theory]
    [InlineData("createdAt")]
    [InlineData("slaDeadlineAt")]
    [InlineData("createdByUserId")]
    [InlineData("id")]
    [InlineData("status")]
    public async Task Authoritative_fields_are_rejected(string field)
    {
        var request = ValidInput(); request[field] = "client-supplied-value";
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/work-orders", request)).StatusCode);
        await AssertNoOrders();
    }

    [Fact]
    public async Task Numbers_are_unique_and_reference_seed_is_repeatable()
    {
        var first = await client.PostAsJsonAsync("/api/v1/work-orders", ValidInput());
        var second = await client.PostAsJsonAsync("/api/v1/work-orders", ValidInput());
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.NotEqual((await first.Content.ReadFromJsonAsync<WorkOrderDetail>(json))!.Number,
            (await second.Content.ReadFromJsonAsync<WorkOrderDetail>(json))!.Number);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceOpsDbContext>();
        await ReferenceDataSeed.SeedAsync(db);
        Assert.Equal(20, await db.Customers.CountAsync());
        Assert.Equal(50, await db.Locations.CountAsync());
        Assert.Equal(15, await db.Technicians.CountAsync());
        Assert.Equal(2, await db.WorkOrders.CountAsync());
    }

    [Fact]
    public async Task Failure_to_insert_activity_rolls_back_the_work_order()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceOpsDbContext>();
        // Test-only database constraint forces the second entity insert to fail.
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"WorkOrderActivities\" ADD CONSTRAINT \"RejectActivityForTest\" CHECK (false) NOT VALID");
        try
        {
            Assert.Equal(HttpStatusCode.InternalServerError, (await client.PostAsJsonAsync("/api/v1/work-orders", ValidInput())).StatusCode);
            await AssertNoOrders();
        }
        finally { await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"WorkOrderActivities\" DROP CONSTRAINT \"RejectActivityForTest\""); }
    }

    [Fact]
    public async Task Missing_detail_and_unauthenticated_create_are_handled()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/work-orders/{Guid.NewGuid()}")).StatusCode);
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/v1/work-orders", ValidInput())).StatusCode);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/work-orders", ValidInput())).StatusCode);
        await AssertNoOrders();
    }

    private async Task SetCsrf()
    {
        var csrf = await client.GetFromJsonAsync<CsrfResponse>("/api/v1/auth/csrf");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf!.Token);
    }

    private async Task AssertNoOrders()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceOpsDbContext>();
        Assert.False(await db.WorkOrders.AnyAsync());
        Assert.False(await db.WorkOrderActivities.AnyAsync());
    }

    public async Task DisposeAsync()
    {
        client?.Dispose();
        if (factory is not null) await factory.DisposeAsync();
        await using var admin = new NpgsqlConnection(adminConnection);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {databaseName} WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 29, 8, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
