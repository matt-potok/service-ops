using Microsoft.AspNetCore.Identity;
using ServiceOps.Api.Identity;

namespace ServiceOps.Api.Persistence.Seeding;

public static class DemoUsers
{
    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration)
    {
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var database = services.GetRequiredService<ServiceOpsDbContext>();
        var accounts = new[]
        {
            (Email: "elena.brooks@atlas.example", Name: "Elena Brooks", Role: "Operations"),
            (Email: "marcus.chen@atlas.example", Name: "Marcus Chen", Role: "Manager")
        };

        // Validate configuration before writing anything. Reruns never reset passwords.
        foreach (var account in accounts)
            if (string.IsNullOrWhiteSpace(configuration[$"Seed:{account.Role}Password"]))
                throw new InvalidOperationException($"Set Seed__{account.Role}Password before seeding demo users.");

        await using var transaction = await database.Database.BeginTransactionAsync();
        foreach (var account in accounts)
        {
            if (!await roles.RoleExistsAsync(account.Role))
                EnsureSucceeded(await roles.CreateAsync(new IdentityRole<Guid>(account.Role)));

            var user = await users.FindByEmailAsync(account.Email);
            if (user is null)
            {
                user = new ApplicationUser
                {
                    Id = Guid.NewGuid(), UserName = account.Email, Email = account.Email,
                    DisplayName = account.Name, EmailConfirmed = true
                };
                EnsureSucceeded(await users.CreateAsync(user, configuration[$"Seed:{account.Role}Password"]!));
            }
            if (!await users.IsInRoleAsync(user, account.Role))
                EnsureSucceeded(await users.AddToRoleAsync(user, account.Role));
        }
        await transaction.CommitAsync();
    }

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(error => error.Description)));
    }
}
