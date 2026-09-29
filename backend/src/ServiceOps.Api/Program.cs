using System.Diagnostics;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ServiceOps.Api.Identity;
using ServiceOps.Api.Persistence;
using ServiceOps.Api.Persistence.Seeding;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
builder.Services.AddDbContext<ServiceOpsDbContext>(options => options.UseNpgsql(
    builder.Configuration.GetConnectionString("ServiceOps")
    ?? throw new InvalidOperationException("Set ConnectionStrings__ServiceOps.")));
builder.Services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
{
    options.User.RequireUniqueEmail = true;
    options.Password.RequiredLength = 12;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
}).AddEntityFrameworkStores<ServiceOpsDbContext>();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "ServiceOps.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
    options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    options.AddPolicy("Operations", policy => policy.RequireRole("Operations", "Manager"));
    options.AddPolicy("Manager", policy => policy.RequireRole("Manager"));
});
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "ServiceOps.Antiforgery";
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
    context.ProblemDetails.Extensions["errorCode"] = context.ProblemDetails.Status switch
    {
        400 => "invalid_request", 401 => "unauthenticated", 403 => "forbidden",
        404 => "not_found", 429 => "too_many_requests", _ => "server_error"
    };
});
builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();
if (args.Contains("--migrate") || args.Contains("--seed"))
{
    await using var scope = app.Services.CreateAsyncScope();
    if (args.Contains("--migrate"))
        await scope.ServiceProvider.GetRequiredService<ServiceOpsDbContext>().Database.MigrateAsync();
    if (args.Contains("--seed"))
    {
        if (!app.Environment.IsDevelopment())
            throw new InvalidOperationException("Demo seeding is only allowed in Development.");
        await DemoUsers.SeedAsync(scope.ServiceProvider, app.Configuration);
    }
    return;
}

app.Use(async (context, next) =>
{
    var started = Stopwatch.GetTimestamp();
    try { await next(context); }
    finally
    {
        app.Logger.LogInformation("HTTP {Method} {Path} returned {StatusCode} in {ElapsedMs}ms; trace {TraceId}",
            context.Request.Method, context.Request.Path, context.Response.StatusCode,
            Stopwatch.GetElapsedTime(started).TotalMilliseconds, context.TraceIdentifier);
    }
});
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health/live", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();
app.MapGet("/health/ready", async (ServiceOpsDbContext database, CancellationToken cancellationToken) =>
    await database.Database.CanConnectAsync(cancellationToken)
        ? Results.Ok(new { status = "ready" })
        : Results.StatusCode(503)).AllowAnonymous();
if (app.Environment.IsDevelopment()) app.MapOpenApi().AllowAnonymous();
app.Run();

public partial class Program;
