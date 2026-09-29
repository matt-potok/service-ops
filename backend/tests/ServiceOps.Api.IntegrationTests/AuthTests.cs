using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ServiceOps.Api.Features.Auth;
using ServiceOps.Api.Persistence;
using ServiceOps.Api.Persistence.Seeding;
using Xunit;

namespace ServiceOps.Api.IntegrationTests;

// Real PostgreSQL, isolated random database; no mocks or authentication bypass.
public sealed class AuthTests : IAsyncLifetime
{
    private readonly string databaseName = "serviceops_test_" + Guid.NewGuid().ToString("N");
    private readonly string adminConnection = Environment.GetEnvironmentVariable("TEST_DATABASE_CONNECTION")
        ?? throw new InvalidOperationException("Set TEST_DATABASE_CONNECTION to a PostgreSQL account with CREATEDB permission.");
    private readonly string password = "Test-only-" + Guid.NewGuid().ToString("N") + "!9";
    private WebApplicationFactory<Program> factory = null!;

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
            builder.ConfigureTestServices(services =>
            {
                services.AddControllers().AddApplicationPart(typeof(RoleProbeController).Assembly);
            });
        });
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ServiceOpsDbContext>().Database.MigrateAsync();
        await DemoUsers.SeedAsync(scope.ServiceProvider, SeedConfiguration());
    }

    private IConfiguration SeedConfiguration() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Seed:OperationsPassword"] = password,
        ["Seed:ManagerPassword"] = password
    }).Build();

    [Theory]
    [InlineData("elena.brooks@atlas.example", "Operations", HttpStatusCode.Forbidden)]
    [InlineData("marcus.chen@atlas.example", "Manager", HttpStatusCode.OK)]
    public async Task Seeded_users_authenticate_and_roles_are_enforced(string email, string role, HttpStatusCode managerStatus)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/test/roles/manager")).StatusCode);
        await Csrf(client);
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        var session = await client.GetFromJsonAsync<SessionResponse>("/api/v1/auth/me");
        Assert.Equal(email, session!.Email);
        Assert.Contains(role, session.Roles);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/test/roles/operations")).StatusCode);
        Assert.Equal(managerStatus, (await client.GetAsync("/test/roles/manager")).StatusCode);
        await Csrf(client); // Identity changed at sign-in.
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/v1/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Invalid_credentials_and_missing_csrf_are_rejected()
    {
        using var client = factory.CreateClient();
        var request = new LoginRequest("elena.brooks@atlas.example", "Wrong-password!9");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/auth/login", request)).StatusCode);
        await Csrf(client);
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Check your email and password", await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Seed_reruns_preserve_users_passwords_and_role_memberships()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceOpsDbContext>();
        var before = await db.Users.AsNoTracking().OrderBy(user => user.Email).Select(user => new { user.Id, user.PasswordHash }).ToArrayAsync();
        await DemoUsers.SeedAsync(scope.ServiceProvider, SeedConfiguration());
        await DemoUsers.SeedAsync(scope.ServiceProvider, SeedConfiguration());
        var after = await db.Users.AsNoTracking().OrderBy(user => user.Email).Select(user => new { user.Id, user.PasswordHash }).ToArrayAsync();
        Assert.Equal(before, after);
        Assert.Equal(2, after.Length);
        Assert.Equal(2, await db.Roles.CountAsync());
        Assert.Equal(2, await db.UserRoles.CountAsync());
    }

    [Fact]
    public async Task Health_is_available_without_authentication()
    {
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
    }

    private static async Task Csrf(HttpClient client)
    {
        var token = await client.GetFromJsonAsync<CsrfResponse>("/api/v1/auth/csrf");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token!.Token);
    }

    public async Task DisposeAsync()
    {
        if (factory is not null) await factory.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(adminConnection);
        await admin.OpenAsync();
        // Name is generated above, never a user-supplied database name.
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {databaseName} WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }
}

// Loaded only by the test host. Phase 1 ships no manager-only product feature.
[ApiController]
[Route("test/roles")]
public sealed class RoleProbeController : ControllerBase
{
    [HttpGet("operations"), Authorize(Policy = "Operations")]
    public IActionResult Operations() => Ok();

    [HttpGet("manager"), Authorize(Policy = "Manager")]
    public IActionResult Manager() => Ok();
}
